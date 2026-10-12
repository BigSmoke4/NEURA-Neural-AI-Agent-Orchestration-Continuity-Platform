using Neura.Modules.ProviderIntegration.Domain;

namespace Neura.Web.ViewModels;

/// <summary>
/// Safe provider-account projection for the Security Center. Deliberately
/// excludes the protected credential payload and user ownership identifiers.
/// </summary>
public sealed record ProviderAccountSummary(
    ProviderKind Kind,
    string DisplayName,
    ProviderConnectionState State,
    DateTime? LastHealthCheckUtc,
    string? LastHealthMessage);
