using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Neura.Infrastructure.Persistence;
using Neura.Modules.Orchestration.Application;
using Xunit;

namespace Neura.Tests.Integration;

public sealed class AccountEmailFlowTests : IAsyncLifetime
{
    private readonly AccountFlowFactory _factory = new();

    [Fact]
    public async Task RegistrationConfirmationAndPasswordResetRequireTokensAndKeepResetResponseGeneric()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        const string email = "account-flow@example.test";
        const string initialPassword = "Initial!Password123";
        const string changedPassword = "Updated!Password2026";

        var (registerToken, _) = await GetAntiforgeryTokenAsync(client, "/Account/Register");
        var registerResponse = await client.PostAsync("/Account/Register", Form(
            ("__RequestVerificationToken", registerToken),
            ("email", email),
            ("password", initialPassword),
            ("role", "User")));

        Assert.Equal(HttpStatusCode.Redirect, registerResponse.StatusCode);
        Assert.Equal("/Account/RegisterConfirmation", registerResponse.Headers.Location?.OriginalString);
        var confirmationMessage = Assert.Single(_factory.EmailSender.Messages);
        Assert.Equal(email, confirmationMessage.To);
        Assert.Contains("Confirm your NEURA account", confirmationMessage.Subject, StringComparison.Ordinal);

        Assert.False(await IsEmailConfirmedAsync(email));
        Assert.True(await CheckPasswordAsync(email, initialPassword));

        var (loginAntiforgery, _) = await GetAntiforgeryTokenAsync(client, "/Account/Login");
        using var blockedLogin = await client.PostAsync("/Account/Login", Form(
            ("__RequestVerificationToken", loginAntiforgery),
            ("email", email),
            ("password", initialPassword)));
        Assert.Equal(HttpStatusCode.OK, blockedLogin.StatusCode);
        Assert.Contains("Invalid email or password", await blockedLogin.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var confirmationUri = ExtractAbsoluteLink(confirmationMessage.Body);
        using var confirmationGet = await client.GetAsync(confirmationUri.PathAndQuery);
        Assert.Equal(HttpStatusCode.OK, confirmationGet.StatusCode);
        Assert.True(confirmationGet.Headers.CacheControl?.NoStore == true);
        Assert.True(confirmationGet.Headers.CacheControl?.NoCache == true);
        Assert.False(await IsEmailConfirmedAsync(email));
        var confirmationHtml = await confirmationGet.Content.ReadAsStringAsync();
        var confirmationAntiforgery = ExtractAntiforgeryToken(confirmationHtml);
        var confirmationQuery = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(confirmationUri.Query);
        var confirmResponse = await client.PostAsync("/Account/ConfirmEmail", Form(
            ("__RequestVerificationToken", confirmationAntiforgery),
            ("userId", confirmationQuery["userId"].ToString()),
            ("token", confirmationQuery["token"].ToString())));

        Assert.Equal(HttpStatusCode.Redirect, confirmResponse.StatusCode);
        Assert.Equal("/Account/EmailConfirmed", confirmResponse.Headers.Location?.OriginalString);
        Assert.True(await IsEmailConfirmedAsync(email));

        var (forgotToken, _) = await GetAntiforgeryTokenAsync(client, "/Account/ForgotPassword");
        var forgotResponse = await client.PostAsync("/Account/ForgotPassword", Form(
            ("__RequestVerificationToken", forgotToken),
            ("email", email)));
        Assert.Equal(HttpStatusCode.Redirect, forgotResponse.StatusCode);
        Assert.Equal("/Account/ForgotPasswordConfirmation", forgotResponse.Headers.Location?.OriginalString);

        var resetMessage = _factory.EmailSender.Messages.Last();
        Assert.Equal("Reset your NEURA password", resetMessage.Subject);
        var resetUri = ExtractAbsoluteLink(resetMessage.Body);
        using var resetGet = await client.GetAsync(resetUri.PathAndQuery);
        Assert.Equal(HttpStatusCode.OK, resetGet.StatusCode);
        Assert.True(resetGet.Headers.CacheControl?.NoStore == true);
        Assert.True(resetGet.Headers.CacheControl?.NoCache == true);
        Assert.True(await CheckPasswordAsync(email, initialPassword));

        var resetHtml = await resetGet.Content.ReadAsStringAsync();
        var resetAntiforgery = ExtractAntiforgeryToken(resetHtml);
        var resetQuery = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(resetUri.Query);
        var resetResponse = await client.PostAsync("/Account/ResetPassword", Form(
            ("__RequestVerificationToken", resetAntiforgery),
            ("userId", resetQuery["userId"].ToString()),
            ("token", resetQuery["token"].ToString()),
            ("password", changedPassword),
            ("confirmPassword", changedPassword)));

        Assert.Equal(HttpStatusCode.Redirect, resetResponse.StatusCode);
        Assert.Equal("/Account/ResetPasswordConfirmation", resetResponse.Headers.Location?.OriginalString);
        Assert.True(await CheckPasswordAsync(email, changedPassword));

        var messagesBeforeUnknownAddress = _factory.EmailSender.Messages.Count;
        var (unknownEmailAntiforgery, _) = await GetAntiforgeryTokenAsync(client, "/Account/ForgotPassword");
        var unknownEmailResponse = await client.PostAsync("/Account/ForgotPassword", Form(
            ("__RequestVerificationToken", unknownEmailAntiforgery),
            ("email", "unknown-account@example.test")));

        Assert.Equal(HttpStatusCode.Redirect, unknownEmailResponse.StatusCode);
        Assert.Equal(forgotResponse.Headers.Location?.OriginalString, unknownEmailResponse.Headers.Location?.OriginalString);
        Assert.Equal(messagesBeforeUnknownAddress, _factory.EmailSender.Messages.Count);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private async Task<bool> IsEmailConfirmedAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);
        return user?.EmailConfirmed == true;
    }

    private async Task<bool> CheckPasswordAsync(string email, string password)
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);
        return user is not null && await userManager.CheckPasswordAsync(user, password);
    }

    private static async Task<(string Token, string Html)> GetAntiforgeryTokenAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        return (ExtractAntiforgeryToken(html), html);
    }

    private static string ExtractAntiforgeryToken(string html)
    {
        var match = Regex.Match(html, "<input[^>]*name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, "The rendered form did not include an antiforgery token.");
        return match.Groups[1].Value;
    }

    private static Uri ExtractAbsoluteLink(string body)
    {
        var match = Regex.Match(body, @"https?://[^\s<>]+");
        Assert.True(match.Success, "The email body did not include an absolute account link.");
        return new Uri(match.Value.TrimEnd('.', ',', ')'));
    }

    private static FormUrlEncodedContent Form(params (string Key, string Value)[] values)
        => new(values.Select(pair => new KeyValuePair<string, string>(pair.Key, pair.Value)));

    private sealed class AccountFlowFactory : WebApplicationFactory<Program>
    {
        private readonly string _databaseName = $"neura-account-flow-{Guid.NewGuid():N}";
        public CapturingEmailSender EmailSender { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(
                    service => service.ServiceType == typeof(DbContextOptions<NeuraDbContext>));
                if (descriptor is not null)
                    services.Remove(descriptor);

                services.AddDbContext<NeuraDbContext>(options => options.UseInMemoryDatabase(_databaseName));
                services.RemoveAll<IEmailSender>();
                services.AddSingleton<IEmailSender>(EmailSender);
                services.PostConfigure<IdentityOptions>(options =>
                {
                    options.SignIn.RequireConfirmedAccount = true;
                    options.SignIn.RequireConfirmedEmail = true;
                });
            });
        }
    }

    private sealed class CapturingEmailSender : IEmailSender
    {
        private readonly ConcurrentQueue<CapturedEmail> _messages = new();
        public IReadOnlyList<CapturedEmail> Messages => _messages.ToArray();

        public Task SendAsync(string toAddress, string subject, string body, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            _messages.Enqueue(new CapturedEmail(toAddress, subject, body));
            return Task.CompletedTask;
        }
    }

    private sealed record CapturedEmail(string To, string Subject, string Body);
}
