using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Neura.Infrastructure.Persistence;
using Xunit;

namespace Neura.Tests.Integration;

public sealed class AccountRateLimitTests : IAsyncLifetime
{
    private readonly RateLimitFactory _factory = new();

    [Fact]
    public async Task Login_and_account_recovery_policies_reject_after_ten_requests()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        using var loginPage = await client.GetAsync("/Account/Login");
        Assert.Equal(HttpStatusCode.OK, loginPage.StatusCode);
        var loginToken = ExtractAntiforgeryToken(await loginPage.Content.ReadAsStringAsync());

        // The login page GET and POST actions share the same named policy.
        // Ten requests from this test host's IP are permitted per minute.
        for (var attempt = 0; attempt < 9; attempt++)
        {
            using var response = await client.PostAsync("/Account/Login", Form(
                ("__RequestVerificationToken", loginToken),
                ("email", "rate-limit-user@example.test"),
                ("password", "Incorrect!Password123")));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        using var rejectedLogin = await client.PostAsync("/Account/Login", Form(
            ("__RequestVerificationToken", loginToken),
            ("email", "rate-limit-user@example.test"),
            ("password", "Incorrect!Password123")));
        Assert.Equal(HttpStatusCode.TooManyRequests, rejectedLogin.StatusCode);

        using var recoveryPage = await client.GetAsync("/Account/ForgotPassword");
        Assert.Equal(HttpStatusCode.OK, recoveryPage.StatusCode);
        var recoveryToken = ExtractAntiforgeryToken(await recoveryPage.Content.ReadAsStringAsync());

        // Recovery uses an independent ten-request window. The GET consumes
        // one permit, followed by nine successful generic unknown-address posts.
        for (var attempt = 0; attempt < 9; attempt++)
        {
            using var response = await client.PostAsync("/Account/ForgotPassword", Form(
                ("__RequestVerificationToken", recoveryToken),
                ("email", "unknown-account@example.test")));
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        }

        using var rejectedRecovery = await client.PostAsync("/Account/ForgotPassword", Form(
            ("__RequestVerificationToken", recoveryToken),
            ("email", "unknown-account@example.test")));
        Assert.Equal(HttpStatusCode.TooManyRequests, rejectedRecovery.StatusCode);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private static string ExtractAntiforgeryToken(string html)
    {
        var match = Regex.Match(html, "<input[^>]*name=\\\"__RequestVerificationToken\\\"[^>]*value=\\\"([^\\\"]+)\\\"");
        Assert.True(match.Success, "The rendered account form did not include an antiforgery token.");
        return match.Groups[1].Value;
    }

    private static FormUrlEncodedContent Form(params (string Key, string Value)[] values)
        => new(values.Select(pair => new KeyValuePair<string, string>(pair.Key, pair.Value)));

    private sealed class RateLimitFactory : WebApplicationFactory<Program>
    {
        private readonly string _databaseName = $"neura-account-rate-limit-{Guid.NewGuid():N}";

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
            });
        }
    }
}
