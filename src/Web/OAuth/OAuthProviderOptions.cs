namespace Neura.Web.OAuth;

/// <summary>
/// Config-driven OAuth2 authorization-code settings per provider kind,
/// read from Neura:OAuth:{ProviderKind} in configuration/user-secrets.
/// Not every AI vendor offers end-user OAuth for API access (several,
/// including Anthropic today, primarily issue API keys) — this flow is
/// provided for the providers/deployments that do support it; the
/// pasted-API-key path in ProvidersController remains available for
/// the rest and is not a fallback hack, just the correct method for
/// those vendors.
/// </summary>
public sealed class OAuthProviderOptions
{
    public string? AuthorizationEndpoint { get; set; }
    public string? TokenEndpoint { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? Scope { get; set; }
}
