namespace Neura.Modules.Orchestration.Application;

/// <summary>Provider boundary for actually delivering one email outside the database.</summary>
public interface IEmailTransport
{
    Task SendAsync(string toAddress, string subject, string body, CancellationToken ct = default);
}
