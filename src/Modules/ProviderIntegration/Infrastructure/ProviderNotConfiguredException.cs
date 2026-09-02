namespace Neura.Modules.ProviderIntegration.Infrastructure;

/// <summary>
/// Thrown when a real-mode provider is invoked without a connected account.
/// Per the "no fake functionality" rule, callers must surface this as
/// NOT CONFIGURED in the UI rather than fabricating a response.
/// </summary>
public sealed class ProviderNotConfiguredException : Exception
{
    public ProviderNotConfiguredException(string providerName)
        : base($"{providerName} is NOT CONFIGURED. Connect this provider in Provider Management before running missions in Real Mode.")
    {
    }
}
