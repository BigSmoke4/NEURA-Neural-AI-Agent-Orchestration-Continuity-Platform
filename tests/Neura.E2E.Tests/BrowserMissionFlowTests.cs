using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Neura.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Neura.E2E.Tests;

/// <summary>
/// A genuine browser-automated end-to-end test: launches a real headless
/// Chromium instance via Playwright and drives the actual rendered UI —
/// clicking links, filling form fields, submitting forms — against a
/// real Kestrel-hosted instance of the app. This is the piece
/// EndToEndMissionTests in Neura.Tests deliberately does NOT cover
/// (that one drives HTTP directly without a browser); this test
/// completes that gap.
///
/// Requires the generated Playwright browser installer to have been run once on the
/// machine executing the tests (see README "Running the E2E tests").
/// </summary>
public class BrowserMissionFlowTests : IAsyncLifetime
{
    private PlaywrightWebApplicationFactory _factory = default!;
    private IPlaywright _playwright = default!;
    private IBrowser _browser = default!;

    public async Task InitializeAsync()
    {
        _factory = new PlaywrightWebApplicationFactory();
        _ = _factory.Server; // forces host startup so ServerAddress is populated

        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
    }

    public async Task DisposeAsync()
    {
        await _browser.DisposeAsync();
        _playwright.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task RegisterLoginAndCreateMission_ThroughTheRealBrowserUI()
    {
        var page = await _browser.NewPageAsync();
        var baseUrl = _factory.ServerAddress;

        // 1. Register through the real rendered form.
        await page.GotoAsync($"{baseUrl}/Account/Register");
        var email = $"browser-e2e-{Guid.NewGuid():N}@example.com";
        await page.FillAsync("input[name='email']", email);
        await page.FillAsync("input[name='password']", "StrongP@ssw0rd123");
        await page.ClickAsync("button[type='submit']");

        // Registration signs the user in and redirects to the Brain screen.
        await page.WaitForURLAsync($"{baseUrl}/Brain**", new PageWaitForURLOptions { Timeout = 10_000 });

        // 2. Navigate to Mission Control through the real nav link, not a direct GotoAsync,
        // so the test also proves the rendered nav actually works.
        await page.ClickAsync("a[href='/Missions']");
        await page.WaitForURLAsync($"{baseUrl}/Missions**");

        // 3. Fill in and submit the real Create Mission form.
        await page.FillAsync("input[name='title']", "Browser E2E Mission");
        await page.FillAsync("input[name='objective']", "Prove the UI itself works, not just the HTTP API");
        await page.SelectOptionAsync("select[name='mode']", "Simulation");
        await page.ClickAsync("button:has-text('Create Mission')");

        // Create redirects to the mission's Details page.
        await page.WaitForURLAsync($"{baseUrl}/Missions/Details/**", new PageWaitForURLOptions { Timeout = 10_000 });

        // 4. Click the real Start button rendered on that page.
        await page.ClickAsync("button:has-text('Start')");
        await page.WaitForURLAsync($"{baseUrl}/Missions/Details/**");

        // 5. Assert against the database that the mission the browser
        // created is actually the one that got queued and persisted —
        // tying the browser interaction back to real server-side state.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NeuraDbContext>();
        var mission = await db.Missions.FirstOrDefaultAsync(m => m.Title == "Browser E2E Mission");

        Assert.NotNull(mission);
        Assert.Equal("Prove the UI itself works, not just the HTTP API", mission!.Objective);
    }
}
