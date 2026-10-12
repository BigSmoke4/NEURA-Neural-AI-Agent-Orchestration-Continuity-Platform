namespace Neura.Modules.Orchestration.Application;

/// <summary>Queues an email for durable delivery; implementations should return after the message is persisted.</summary>
public interface IEmailSender
{
    Task SendAsync(string toAddress, string subject, string body, CancellationToken ct = default);
}
