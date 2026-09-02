using Microsoft.AspNetCore.DataProtection;

namespace Neura.Infrastructure.Security;

public sealed class DataProtectionCredentialProtector : ICredentialProtector
{
    private const string Purpose = "Neura.ProviderCredentials.v1";
    private readonly IDataProtector _protector;

    public DataProtectionCredentialProtector(IDataProtectionProvider provider)
        => _protector = provider.CreateProtector(Purpose);

    public string Protect(string plaintextSecret) => _protector.Protect(plaintextSecret);

    public string Unprotect(string protectedSecret) => _protector.Unprotect(protectedSecret);
}
