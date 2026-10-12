namespace Neura.Infrastructure.Persistence;

internal sealed record EmailOutboxPayload(string ToAddress, string Subject, string Body);
