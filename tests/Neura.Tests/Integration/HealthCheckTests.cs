using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Neura.Infrastructure.Persistence;
using Xunit;
using System.Linq;

namespace Neura.Tests.Integration;

/// <summary>
/// Real integration tests against the full ASP.NET Core pipeline via
/// WebApplicationFactory — replaces Postgres with EF Core's InMemory
/// provider so these run without a database dependency in CI, while
/// still exercising DI wiring, middleware, routing, and controllers
/// end-to-end. This covers a slice of section 66's "integration tests"
/// requirement (real HTTP pipeline, real DI graph) — it does not
/// replace testing against a real Postgres instance (e.g. via
/// Testcontainers) before a production deploy.
/// </summary>
public class NeuraWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"neura-integration-tests-{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<NeuraDbContext>));
            if (descriptor is not null) services.Remove(descriptor);

            services.AddDbContext<NeuraDbContext>(options =>
                options.UseInMemoryDatabase(_databaseName));
        });
    }
}

public class HealthCheckTests : IClassFixture<NeuraWebApplicationFactory>
{
    private readonly NeuraWebApplicationFactory _factory;

    public HealthCheckTests(NeuraWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        return client;
    }

    private static string ExtractAntiforgeryToken(string html)
    {
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    [Fact]
    public async Task Health_ReturnsHealthy()
    {
        using var client = CreateClient();
        var response = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task BrainDashboard_RedirectsAnonymousUserToLogin()
    {
        // Brain is [Authorize]-protected; an anonymous request should be
        // redirected to the login page rather than served directly —
        // this is asserting real authorization middleware behavior, not
        // a mocked check.
        using var client = CreateClient();
        var response = await client.GetAsync("/Brain");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString() ?? string.Empty);
    }

    [Fact]
    public async Task Register_ThenLogin_Succeeds()
    {
        using var client = CreateClient();
        var email = $"test-{Guid.NewGuid():N}@example.com";
        const string password = "StrongP@ssw0rd123";

        // Exercise the real browser-style antiforgery handshake: GET emits
        // both the antiforgery cookie and hidden form token, then POST sends
        // the token back with the same cookie jar.
        var registerPage = await client.GetAsync("/Account/Register");
        Assert.Equal(HttpStatusCode.OK, registerPage.StatusCode);
        var registerToken = ExtractAntiforgeryToken(await registerPage.Content.ReadAsStringAsync());
        Assert.False(string.IsNullOrWhiteSpace(registerToken));

        var registerResponse = await client.PostAsync("/Account/Register", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = registerToken,
            ["email"] = email,
            ["password"] = password,
            ["role"] = "User"
        }));

        Assert.Equal(HttpStatusCode.Redirect, registerResponse.StatusCode);
        Assert.Equal("/Brain", registerResponse.Headers.Location?.ToString());

        // Registration signs the user in. Verify the authenticated session
        // before explicitly signing out.
        var brainResponse = await client.GetAsync("/Brain");
        Assert.Equal(HttpStatusCode.OK, brainResponse.StatusCode);
        var brainHtml = await brainResponse.Content.ReadAsStringAsync();
        var logoutToken = ExtractAntiforgeryToken(brainHtml);
        Assert.False(string.IsNullOrWhiteSpace(logoutToken));

        var logoutResponse = await client.PostAsync("/Account/Logout", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = logoutToken
        }));
        Assert.Equal(HttpStatusCode.Redirect, logoutResponse.StatusCode);

        // Now prove the persisted Identity account can authenticate again.
        var loginPage = await client.GetAsync("/Account/Login");
        Assert.Equal(HttpStatusCode.OK, loginPage.StatusCode);
        var loginToken = ExtractAntiforgeryToken(await loginPage.Content.ReadAsStringAsync());
        Assert.False(string.IsNullOrWhiteSpace(loginToken));

        var loginResponse = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = loginToken,
            ["email"] = email,
            ["password"] = password
        }));

        Assert.Equal(HttpStatusCode.Redirect, loginResponse.StatusCode);
        Assert.Equal("/Brain", loginResponse.Headers.Location?.ToString());

        var authenticatedBrain = await client.GetAsync("/Brain");
        Assert.Equal(HttpStatusCode.OK, authenticatedBrain.StatusCode);
    }
}
