namespace Neura.Modules.ContextManagement.Domain;

/// <summary>
/// Section 60: trust classification for any text entering an agent's
/// context. Content pulled from outside the system (web pages,
/// documents, another agent's output) must never be silently treated as
/// instructions with the same authority as the system prompt or the
/// user's own mission text.
/// </summary>
public enum ContentTrustLevel
{
    SystemInstruction,
    AgentPolicy,
    UserMission,
    TrustedProjectContext,
    UntrustedExternalContent,
    AgentOutput
}

/// <summary>
/// A single piece of text tagged with where it came from. Anything built
/// from UntrustedExternalContent should be rendered to the receiving
/// agent as clearly-delimited, non-authoritative reference material —
/// never concatenated indistinguishably into the same instruction
/// stream as SystemInstruction or UserMission content.
/// </summary>
public sealed record TrustLabeledContent(string Text, ContentTrustLevel TrustLevel, string? SourceDescription = null)
{
    public bool IsTrusted => TrustLevel is ContentTrustLevel.SystemInstruction
        or ContentTrustLevel.AgentPolicy
        or ContentTrustLevel.UserMission
        or ContentTrustLevel.TrustedProjectContext;
}
