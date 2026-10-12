using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Neura.Infrastructure.Email;
using Neura.Infrastructure.Persistence;
using Neura.Modules.Orchestration.Application;
using Xunit;

namespace Neura.Tests.Integration;

public sealed class EmailOutboxTests
{
    [Fact]
    public async Task Enqueue_PersistsEncryptedPayloadBeforeReturning()
    {
        using var services = CreateServices(new RecordingTransport());
        var sender = services.GetRequiredService<IEmailSender>();
        const string body = "password-reset-token-must-not-be-stored-in-plaintext";

        await sender.SendAsync("person@example.test", "Reset your password", body);

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NeuraDbContext>();
        var message = await db.EmailOutboxMessages.SingleAsync();
        Assert.NotNull(message.ProtectedPayload);
        Assert.DoesNotContain(body, message.ProtectedPayload!, StringComparison.Ordinal);
        Assert.Null(message.SentAtUtc);
        Assert.Equal(0, message.AttemptCount);

        var protector = services.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("Neura.EmailOutbox.Payload.v1");
        using var payload = JsonDocument.Parse(protector.Unprotect(message.ProtectedPayload!));
        Assert.Equal(body, payload.RootElement.GetProperty("body").GetString());
    }

    [Fact]
    public async Task Dispatcher_SendsThenErasesProtectedPayload()
    {
        var transport = new RecordingTransport();
        using var services = CreateServices(transport);
        var sender = services.GetRequiredService<IEmailSender>();
        await sender.SendAsync("person@example.test", "Confirmation", "confirm-token");

        var dispatcher = services.GetRequiredService<IEmailOutboxDispatcher>();
        Assert.True(await dispatcher.DispatchNextAsync(CancellationToken.None));
        Assert.False(await dispatcher.DispatchNextAsync(CancellationToken.None));

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NeuraDbContext>();
        var message = await db.EmailOutboxMessages.SingleAsync();
        Assert.Equal(DateTimeKind.Utc, message.SentAtUtc!.Value.Kind);
        Assert.Null(message.ProtectedPayload);
        Assert.Null(message.LeaseId);
        Assert.Equal(1, message.AttemptCount);
        Assert.Equal("person@example.test", Assert.Single(transport.Messages).ToAddress);
        Assert.Equal("confirm-token", transport.Messages[0].Body);
    }

    [Fact]
    public async Task FailedTransport_IsRetriedWithoutPersistingRemoteErrorText()
    {
        var transport = new RecordingTransport { Failure = new InvalidOperationException("remote response included a secret") };
        using var services = CreateServices(transport, new EmailOutboxOptions { MaxAttempts = 3 });
        await services.GetRequiredService<IEmailSender>()
            .SendAsync("person@example.test", "Confirmation", "confirm-token");

        Assert.True(await services.GetRequiredService<IEmailOutboxDispatcher>()
            .DispatchNextAsync(CancellationToken.None));

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NeuraDbContext>();
        var message = await db.EmailOutboxMessages.SingleAsync();
        Assert.Equal(1, message.AttemptCount);
        Assert.Null(message.LeaseId);
        Assert.Null(message.DeadLetteredAtUtc);
        Assert.True(message.NextAttemptAtUtc > DateTime.UtcNow);
        Assert.Equal(nameof(InvalidOperationException), message.LastFailureType);
        Assert.NotNull(message.ProtectedPayload);
        Assert.DoesNotContain("remote response included a secret", message.LastFailureType!, StringComparison.Ordinal);
        var protector = services.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("Neura.EmailOutbox.Payload.v1");
        var persistedPayload = protector.Unprotect(message.ProtectedPayload!);
        Assert.Contains("confirm-token", persistedPayload, StringComparison.Ordinal);
        Assert.DoesNotContain("remote response included a secret", persistedPayload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FirstFailure_DeadLettersWhenConfiguredAttemptLimitIsOne()
    {
        var transport = new RecordingTransport { Failure = new InvalidOperationException("sensitive server reply") };
        using var services = CreateServices(transport, new EmailOutboxOptions { MaxAttempts = 1 });
        await services.GetRequiredService<IEmailSender>()
            .SendAsync("person@example.test", "Confirmation", "confirm-token");

        Assert.True(await services.GetRequiredService<IEmailOutboxDispatcher>()
            .DispatchNextAsync(CancellationToken.None));

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NeuraDbContext>();
        var message = await db.EmailOutboxMessages.SingleAsync();
        Assert.Equal(1, message.AttemptCount);
        Assert.NotNull(message.DeadLetteredAtUtc);
        Assert.NotNull(message.ProtectedPayload);
        Assert.Equal(nameof(InvalidOperationException), message.LastFailureType);
    }

    private static ServiceProvider CreateServices(IEmailTransport transport, EmailOutboxOptions? options = null)
    {
        var services = new ServiceCollection();
        var databaseName = $"email-outbox-{Guid.NewGuid():N}";
        services.AddLogging();
        services.AddDataProtection().SetApplicationName("Neura.EmailOutbox.Tests");
        services.AddDbContext<NeuraDbContext>(builder => builder.UseInMemoryDatabase(databaseName));
        services.Configure<EmailOutboxOptions>(configured =>
        {
            configured.PollIntervalSeconds = options?.PollIntervalSeconds ?? 1;
            configured.LeaseDurationSeconds = options?.LeaseDurationSeconds ?? 60;
            configured.MaxAttempts = options?.MaxAttempts ?? 12;
            configured.InitialRetryDelaySeconds = options?.InitialRetryDelaySeconds ?? 1;
            configured.MaximumRetryDelaySeconds = options?.MaximumRetryDelaySeconds ?? 10;
        });
        services.AddSingleton<IEmailTransport>(transport);
        services.AddScoped<IEmailSender, EfEmailOutboxSender>();
        services.AddSingleton<IEmailOutboxDispatcher, EfEmailOutboxDispatcher>();
        return services.BuildServiceProvider();
    }

    private sealed class RecordingTransport : IEmailTransport
    {
        public List<SentEmail> Messages { get; } = new();
        public Exception? Failure { get; init; }

        public Task SendAsync(string toAddress, string subject, string body, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (Failure is not null)
                throw Failure;

            Messages.Add(new SentEmail(toAddress, subject, body));
            return Task.CompletedTask;
        }
    }

    private sealed record SentEmail(string ToAddress, string Subject, string Body);
}
