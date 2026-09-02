namespace Neura.Modules.ProviderIntegration.Domain;

public enum ProviderKind
{
    OpenAI,
    Anthropic,
    Google,
    LocalModel,
    Simulation
}

public enum ProviderConnectionState
{
    NotConfigured,
    Connected,
    Failing,
    Disconnected
}

/// <summary>
/// Represents an AI provider account the user has explicitly connected.
/// Credentials are never stored in plaintext; only a reference to a
/// protected secret (Data Protection / secret store) is kept here.
/// </summary>
public class AIProviderAccount
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid UserId { get; private set; }
    public ProviderKind Kind { get; private set; }
    public string DisplayName { get; private set; } = default!;
    public string ProtectedCredentialRef { get; private set; } = default!;
    public ProviderConnectionState State { get; private set; } = ProviderConnectionState.NotConfigured;
    public DateTime? LastHealthCheckUtc { get; private set; }
    public string? LastHealthMessage { get; private set; }
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;

    private AIProviderAccount() { }

    public static AIProviderAccount Connect(Guid userId, ProviderKind kind, string displayName, string protectedCredentialRef)
    {
        return new AIProviderAccount
        {
            UserId = userId,
            Kind = kind,
            DisplayName = displayName,
            ProtectedCredentialRef = protectedCredentialRef,
            State = ProviderConnectionState.Connected
        };
    }

    public void RecordHealth(bool healthy, string? message)
    {
        LastHealthCheckUtc = DateTime.UtcNow;
        LastHealthMessage = message;
        State = healthy ? ProviderConnectionState.Connected : ProviderConnectionState.Failing;
    }

    public void Disconnect() => State = ProviderConnectionState.Disconnected;

    /// <summary>Section 37: credential rotation — fully replaces the encrypted credential.</summary>
    public void Rotate(string newProtectedCredentialRef)
    {
        ProtectedCredentialRef = newProtectedCredentialRef;
        State = ProviderConnectionState.Connected;
    }
}
