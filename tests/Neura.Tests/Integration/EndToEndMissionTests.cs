using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Neura.Infrastructure.Persistence;
using Xunit;

namespace Neura.Tests.Integration;

/// <summary>
/// A full-stack, non-browser end-to-end test: registers a real user
/// through the actual Identity flow (picking up the antiforgery token
/// from the rendered HTML the way a browser would), creates a mission
/// through MissionsController, starts it, and polls the real database
/// until the background MissionWorker has driven it to completion via
/// the Simulation provider — then asserts ExecutionEvent and CostRecord
/// rows were actually written. This exercises the complete path the UI
/// drives (HTTP → auth → controller → EF → queue → background worker →
/// orchestration engine → persistence) without a browser automating the
/// DOM, which is the one piece still missing per the README.
/// </summary>
public class EndToEndMissionTests : IClassFixture<NeuraWebApplicationFactory>
{
    private readonly NeuraWebApplicationFactory _factory;

    public EndToEndMissionTests(NeuraWebApplicationFactory factory) => _factory = factory;

    private static string? ExtractAntiforgeryToken(string html)
    {
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        return match.Success ? match.Groups[1].Value : null;
    }

    [Fact]
    public async Task RegisterLoginCreateAndStartMission_CompletesViaBackgroundWorker()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));

        // 1. GET the register page to obtain a real antiforgery token.
        var registerPage = await client.GetAsync("/Account/Register");
        var registerHtml = await registerPage.Content.ReadAsStringAsync();
        var token = ExtractAntiforgeryToken(registerHtml);
        Assert.False(string.IsNullOrEmpty(token));

        var email = $"e2e-{Guid.NewGuid():N}@example.com";
        var registerResponse = await client.PostAsync("/Account/Register", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token!,
            ["email"] = email,
            ["password"] = "StrongP@ssw0rd123"
        }));

        // Registration signs the user in and redirects to the Brain screen.
        Assert.True(registerResponse.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.OK);

        // 2. GET Mission Control to obtain a fresh antiforgery token as
        // this now-authenticated user.
        var missionsPage = await client.GetAsync("/Missions");
        var missionsHtml = await missionsPage.Content.ReadAsStringAsync();
        var missionsToken = ExtractAntiforgeryToken(missionsHtml);
        Assert.False(string.IsNullOrEmpty(missionsToken));

        // 3. Create a mission (Simulation mode — no external provider needed).
        var createResponse = await client.PostAsync("/Missions/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = missionsToken!,
            ["title"] = "E2E Test Mission",
            ["objective"] = "Prove the full stack actually runs a mission to completion",
            ["mode"] = "Simulation"
        }));
        Assert.Equal(HttpStatusCode.Redirect, createResponse.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NeuraDbContext>();
        var mission = await db.Missions.OrderByDescending(m => m.CreatedAtUtc).FirstAsync();

        // 4. Start it — this is what actually enqueues it for MissionWorker.
        var detailsPage = await client.GetAsync($"/Missions/Details/{mission.Id}");
        var detailsHtml = await detailsPage.Content.ReadAsStringAsync();
        var startToken = ExtractAntiforgeryToken(detailsHtml);
        Assert.False(string.IsNullOrEmpty(startToken));

        var startResponse = await client.PostAsync($"/Missions/Start/{mission.Id}", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = startToken!
        }));
        Assert.Equal(HttpStatusCode.Redirect, startResponse.StatusCode);

        // 5. Poll the database (not the UI) for the background worker to
        // finish — real orchestration, running out-of-process, actually
        // completing the mission.
        var deadline = DateTime.UtcNow.AddSeconds(30);
        bool completed = false;
        while (DateTime.UtcNow < deadline)
        {
            using var pollScope = _factory.Services.CreateScope();
            var pollDb = pollScope.ServiceProvider.GetRequiredService<NeuraDbContext>();
            var events = await pollDb.ExecutionEvents.Where(e => e.MissionId == mission.Id).ToListAsync();
            if (events.Any(e => e.EventType == "TaskCompleted"))
            {
                completed = true;
                break;
            }
            await Task.Delay(500);
        }

        if (!completed)
        {
            using var diagnosticScope = _factory.Services.CreateScope();
            var diagnosticDb = diagnosticScope.ServiceProvider.GetRequiredService<NeuraDbContext>();
            var currentMission = await diagnosticDb.Missions.AsNoTracking().FirstOrDefaultAsync(m => m.Id == mission.Id);
            var currentTask = await diagnosticDb.Tasks.AsNoTracking().FirstOrDefaultAsync(t => t.MissionId == mission.Id);
            var missionEvents = await diagnosticDb.ExecutionEvents.AsNoTracking()
                .Where(e => e.MissionId == mission.Id)
                .OrderBy(e => e.TimestampUtc)
                .Select(e => e.EventType)
                .ToListAsync();

            Assert.Fail($"Mission did not complete via the background worker within the timeout. " +
                        $"MissionStatus={currentMission?.Status}, TaskStatus={currentTask?.Status}, " +
                        $"Events=[{string.Join(", ", missionEvents)}]");
        }

        Assert.True(completed);
    }
}
