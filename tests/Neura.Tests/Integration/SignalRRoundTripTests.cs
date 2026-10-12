using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Neura.Infrastructure.Persistence;
using Neura.Modules.Orchestration.Application;
using Xunit;

namespace Neura.Tests.Integration;

/// <summary>Exercises authenticated SignalR negotiation and owner-scoped event delivery.</summary>
public class SignalRRoundTripTests : IClassFixture<NeuraWebApplicationFactory>, IAsyncLifetime
{
    private readonly NeuraWebApplicationFactory _factory;
    private HubConnection? _connection;
    private Uri _baseAddress = default!;
    private Guid _ownerUserId;

    public SignalRRoundTripTests(NeuraWebApplicationFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        var cookies = new CookieContainer();
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false
        });
        _baseAddress = client.BaseAddress!;

        var registerPage = await client.GetAsync("/Account/Register");
        CaptureCookies(registerPage, cookies, client.BaseAddress!);
        var html = await registerPage.Content.ReadAsStringAsync();
        var tokenMatch = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        if (!tokenMatch.Success)
            throw new InvalidOperationException("Register page did not render an antiforgery token.");

        var ownerEmail = $"signalr-{Guid.NewGuid():N}@example.com";
        using var register = new HttpRequestMessage(HttpMethod.Post, "/Account/Register")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = tokenMatch.Groups[1].Value,
                ["email"] = ownerEmail,
                ["password"] = "StrongP@ssw0rd123",
                ["role"] = "User"
            })
        };
        register.Headers.Add("Cookie", cookies.GetCookieHeader(new Uri(client.BaseAddress!, "/Account/Register")));
        var registered = await client.SendAsync(register);
        CaptureCookies(registered, cookies, client.BaseAddress!);
        if (registered.StatusCode != HttpStatusCode.Redirect)
            throw new InvalidOperationException("Could not establish an authenticated SignalR test session.");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NeuraDbContext>();
            _ownerUserId = await db.Users.Where(user => user.Email == ownerEmail)
                .Select(user => user.Id)
                .SingleAsync();
        }

        _connection = new HubConnectionBuilder()
            .WithUrl(new Uri(_baseAddress, "/hubs/neural"), options =>
            {
                // TestServer's custom handler is not an HttpClientHandler, so it
                // cannot apply HttpConnectionOptions.Cookies itself. Forward the
                // authenticated cookies explicitly on SignalR's HTTP requests.
                options.Headers["Cookie"] = GetHubCookieHeader(cookies);
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
            })
            .Build();

        await _connection.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_connection is not null)
            await _connection.DisposeAsync();
    }

    [Fact]
    public void Hub_AcceptsAnAuthenticatedConnection()
        => Assert.Equal(HubConnectionState.Connected, _connection!.State);

    [Fact]
    public async Task Publisher_SendsMissionEventsOnlyToTheOwningUserGroup()
    {
        var otherCookies = await RegisterAdditionalUserAsync();
        await using var otherConnection = new HubConnectionBuilder()
            .WithUrl(new Uri(_baseAddress, "/hubs/neural"), options =>
            {
                options.Headers["Cookie"] = GetHubCookieHeader(otherCookies);
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
            })
            .Build();

        var ownerReceived = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var otherReceived = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        static bool IsProbe(JsonElement envelope)
            => envelope.TryGetProperty("type", out var type) && type.GetString() == "TenantScopeProbe";

        _connection!.On<JsonElement>("NeuralEvent", envelope =>
        {
            if (IsProbe(envelope)) ownerReceived.TrySetResult(true);
        });
        otherConnection.On<JsonElement>("NeuralEvent", envelope =>
        {
            if (IsProbe(envelope)) otherReceived.TrySetResult(true);
        });
        await otherConnection.StartAsync();

        var publisher = _factory.Services.GetRequiredService<INeuralEventPublisher>();
        await publisher.PublishAsync("TenantScopeProbe", new
        {
            MissionId = Guid.NewGuid(),
            OwnerUserId = _ownerUserId,
            TaskId = Guid.NewGuid(),
            AgentId = Guid.NewGuid()
        });

        await ownerReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(TimeSpan.FromMilliseconds(250));
        Assert.False(otherReceived.Task.IsCompleted, "A different authenticated user received another user's mission event.");
    }

    private string GetHubCookieHeader(CookieContainer cookies)
    {
        var header = cookies.GetCookieHeader(new Uri(_baseAddress, "/hubs/neural"));
        if (!header.Contains(".AspNetCore.Identity.Application=", StringComparison.Ordinal))
            throw new InvalidOperationException("The authenticated Identity cookie was not captured for the SignalR connection.");

        return header;
    }

    private async Task<CookieContainer> RegisterAdditionalUserAsync()
    {
        var cookies = new CookieContainer();
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false
        });

        var registerPage = await client.GetAsync("/Account/Register");
        CaptureCookies(registerPage, cookies, client.BaseAddress!);
        var html = await registerPage.Content.ReadAsStringAsync();
        var tokenMatch = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        if (!tokenMatch.Success)
            throw new InvalidOperationException("Register page did not render an antiforgery token.");

        using var register = new HttpRequestMessage(HttpMethod.Post, "/Account/Register")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = tokenMatch.Groups[1].Value,
                ["email"] = $"signalr-other-{Guid.NewGuid():N}@example.com",
                ["password"] = "StrongP@ssw0rd123",
                ["role"] = "User"
            })
        };
        register.Headers.Add("Cookie", cookies.GetCookieHeader(new Uri(client.BaseAddress!, "/Account/Register")));
        var registered = await client.SendAsync(register);
        CaptureCookies(registered, cookies, client.BaseAddress!);
        if (registered.StatusCode != HttpStatusCode.Redirect)
            throw new InvalidOperationException("Could not establish a second authenticated SignalR test session.");

        return cookies;
    }

    private static void CaptureCookies(HttpResponseMessage response, CookieContainer cookies, Uri baseAddress)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values))
            return;

        foreach (var value in values)
        {
            try
            {
                cookies.SetCookies(baseAddress, value);
            }
            catch (CookieException)
            {
                // Ignore cookie attributes that the test container cannot store.
            }
        }
    }
}
