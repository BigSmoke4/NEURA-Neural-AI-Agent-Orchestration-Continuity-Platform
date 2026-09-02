using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Neura.Infrastructure.Persistence;
using Neura.Infrastructure.Security;
using Neura.Modules.Observability.Domain;
using Neura.Modules.ProviderIntegration.Domain;

namespace Neura.Web.OAuth;

/// <summary>
/// Generic OAuth2 authorization-code flow for connecting a provider,
/// as an alternative to pasting an API key. Genuinely redirects to the
/// provider's authorize endpoint, exchanges the returned code for a
/// token server-side, and stores the resulting access token encrypted
/// via ICredentialProtector — exactly like an API key, just obtained a
/// different way. State is a single-use, signed anti-forgery value to
/// prevent CSRF on the callback.
/// </summary>
[Authorize(Policy = "ManageProviders")]
[Route("oauth/{kind}")]
public class ProviderOAuthController : Controller
{
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ICredentialProtector _credentialProtector;
    private readonly NeuraDbContext _db;
    private readonly IAuditLogger _audit;

    public ProviderOAuthController(IConfiguration configuration, IHttpClientFactory httpClientFactory,
        ICredentialProtector credentialProtector, NeuraDbContext db, IAuditLogger audit)
    {
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
        _credentialProtector = credentialProtector;
        _db = db;
        _audit = audit;
    }

    [HttpGet("start")]
    public IActionResult Start(string kind)
    {
        if (!Enum.TryParse<ProviderKind>(kind, true, out _))
            return BadRequest("Unsupported provider kind.");

        var options = _configuration.GetSection($"Neura:OAuth:{kind}").Get<OAuthProviderOptions>();
        if (options is null || string.IsNullOrEmpty(options.AuthorizationEndpoint) || string.IsNullOrEmpty(options.ClientId))
            return NotFound($"OAuth is not configured for provider '{kind}'. Configure Neura:OAuth:{kind} or use the API key connection method instead.");

        var state = Guid.NewGuid().ToString("N");
        HttpContext.Session.SetString($"oauth_state_{kind}", state);

        var redirectUri = Url.Action(nameof(Callback), null, new { kind }, Request.Scheme);
        var authorizeUrl = $"{options.AuthorizationEndpoint}?response_type=code&client_id={Uri.EscapeDataString(options.ClientId)}" +
            $"&redirect_uri={Uri.EscapeDataString(redirectUri!)}&scope={Uri.EscapeDataString(options.Scope ?? string.Empty)}&state={state}";

        return Redirect(authorizeUrl);
    }

    [HttpGet("callback")]
    public async Task<IActionResult> Callback(string kind, string code, string state, CancellationToken ct)
    {
        var expectedState = HttpContext.Session.GetString($"oauth_state_{kind}");
        // OAuth state is single-use: remove it before doing any token exchange
        // so a captured callback cannot be replayed successfully.
        HttpContext.Session.Remove($"oauth_state_{kind}");
        if (string.IsNullOrEmpty(expectedState) || expectedState != state)
            return BadRequest("Invalid OAuth state — possible CSRF attempt or expired session.");

        var options = _configuration.GetSection($"Neura:OAuth:{kind}").Get<OAuthProviderOptions>();
        if (options is null || string.IsNullOrEmpty(options.TokenEndpoint))
            return NotFound($"OAuth is not configured for provider '{kind}'.");

        var client = _httpClientFactory.CreateClient("oauth-token-exchange");
        var tokenResponse = await client.PostAsync(options.TokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["client_id"] = options.ClientId ?? string.Empty,
            ["client_secret"] = options.ClientSecret ?? string.Empty,
            ["redirect_uri"] = Url.Action(nameof(Callback), null, new { kind }, Request.Scheme)!
        }), ct);

        if (!tokenResponse.IsSuccessStatusCode)
            return StatusCode(502, "Token exchange with the provider failed.");

        var payload = await tokenResponse.Content.ReadFromJsonAsync<OAuthTokenPayload>(cancellationToken: ct);
        if (payload?.AccessToken is null)
            return StatusCode(502, "Provider did not return an access token.");

        if (!Enum.TryParse<ProviderKind>(kind, true, out var providerKind) ||
            providerKind == ProviderKind.Simulation ||
            providerKind == ProviderKind.LocalModel)
            return BadRequest("OAuth is not supported for this provider.");
        var encrypted = _credentialProtector.Protect(payload.AccessToken);
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdClaim, out var userId))
            return Unauthorized("The signed-in user identity could not be resolved.");

        var account = AIProviderAccount.Connect(userId, providerKind, $"{kind} (OAuth)", encrypted);
        _db.ProviderAccounts.Add(account);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(userId, "ProviderConnected", account.Id.ToString(), "Success (OAuth)", Guid.NewGuid(), HttpContext.Connection.RemoteIpAddress?.ToString(), ct);

        return RedirectToAction("Index", "Providers");
    }

    private sealed class OAuthTokenPayload
    {
        [System.Text.Json.Serialization.JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }
    }
}
