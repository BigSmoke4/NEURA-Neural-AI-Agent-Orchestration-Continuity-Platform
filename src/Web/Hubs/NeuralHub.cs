using Microsoft.AspNetCore.SignalR;
using Neura.Modules.Orchestration.Application;

namespace Neura.Web.Hubs;

/// <summary>
/// SignalR hub broadcasting real neural activity events to the Brain
/// dashboard. Clients join the "brain" group to receive live updates.
/// </summary>
public class NeuralHub : Hub
{
    public const string BrainGroup = "brain";

    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, BrainGroup);
        await base.OnConnectedAsync();
    }
}

public sealed class SignalRNeuralEventPublisher : INeuralEventPublisher
{
    private readonly IHubContext<NeuralHub> _hub;

    public SignalRNeuralEventPublisher(IHubContext<NeuralHub> hub) => _hub = hub;

    public Task PublishAsync(string eventType, object payload, CancellationToken ct = default)
        => _hub.Clients.Group(NeuralHub.BrainGroup).SendAsync("NeuralEvent", new
        {
            type = eventType,
            timestamp = DateTime.UtcNow,
            payload
        }, ct);
}
