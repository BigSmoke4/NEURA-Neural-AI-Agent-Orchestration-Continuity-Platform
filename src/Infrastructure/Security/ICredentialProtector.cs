namespace Neura.Infrastructure.Security;

/// <summary>
/// Encrypts provider API keys at rest using ASP.NET Core Data Protection
/// before they're persisted as AIProviderAccount.ProtectedCredentialRef,
/// and decrypts them only at the point of use (building a real-mode
/// IAIProvider). Keys are never logged, never returned to a view, and
/// never stored in plaintext in the database.
/// </summary>
public interface ICredentialProtector
{
    string Protect(string plaintextSecret);
    string Unprotect(string protectedSecret);
}
