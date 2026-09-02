namespace Neura.Modules.Orchestration.Application;

/// <summary>Section 79: optional email delivery channel for notifications.</summary>
public interface IEmailSender
{
    Task SendAsync(string toAddress, string subject, string body, CancellationToken ct = default);
}
