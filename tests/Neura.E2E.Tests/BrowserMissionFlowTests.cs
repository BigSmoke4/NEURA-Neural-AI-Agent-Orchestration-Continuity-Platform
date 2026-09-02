using Microsoft.Playwright;
using Xunit;

namespace Neura.E2E.Tests;

/// <summary>
/// A genuine browser-automated end-to-end test. The CI workflow starts the
/// published NEURA web application on a real Kestrel TCP port, and Playwright
/// drives that externally reachable application exactly like a real browser.
/// </summary>
public class BrowserMissionFlowTests : IAsyncLifetime
{
    private IPlaywright _playwright = default!;
    private IBrowser _browser = default!;

    public async Task InitializeAsync()
    {
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
    }

    public async Task DisposeAsync()
    {
        await _browser.DisposeAsync();
        _playwright.Dispose();
    }

    [Fact]
    public async Task RegisterLoginAndCreateMission_ThroughTheRealBrowserUI()
    {
        var baseUrl = Environment.GetEnvironmentVariable("NEURA_E2E_BASE_URL") ?? "http://127.0.0.1:5080";
        var page = await _browser.NewPageAsync();

        // 1. Register through the real rendered form.
        await page.GotoAsync($"{baseUrl}/Account/Register");
        var email = $"browser-e2e-{Guid.NewGuid():N}@example.com";
        await page.FillAsync("input[name='email']", email);
        await page.FillAsync("input[name='password']", "StrongP@ssw0rd123");
        await page.ClickAsync("button[type='submit']");

        // Registration signs the user in and redirects to the Brain screen.
        await page.WaitForURLAsync($"{baseUrl}/Brain**", new PageWaitForURLOptions { Timeout = 10_000 });

        // 2. Navigate through the rendered navigation.
        await page.ClickAsync("a[href='/Missions']");
        await page.WaitForURLAsync($"{baseUrl}/Missions**");

        // 3. Fill in and submit the real Create Mission form.
        await page.FillAsync("input[name='title']", "Browser E2E Mission");
        await page.FillAsync("input[name='objective']", "Prove the UI itself works, not just the HTTP API");
        await page.SelectOptionAsync("select[name='mode']", "Simulation");
        await page.ClickAsync("button:has-text('Create Mission')");

        // Create redirects to the mission Details page.
        await page.WaitForURLAsync($"{baseUrl}/Missions/Details/**", new PageWaitForURLOptions { Timeout = 10_000 });

        // The browser-created mission is rendered by the actual application,
        // proving the POST, persistence, redirect, authorization and view all worked.
        await ExpectTextAsync(page, "Browser E2E Mission");
        await ExpectTextAsync(page, "Prove the UI itself works, not just the HTTP API");

        // 4. Click the real Start button rendered on that page.
        await page.ClickAsync("button:has-text('Start')");
        await page.WaitForURLAsync($"{baseUrl}/Missions/Details/**");
    }

    private static async Task ExpectTextAsync(IPage page, string text)
    {
        var body = await page.Locator("body").InnerTextAsync();
        Assert.Contains(text, body);
    }
}
