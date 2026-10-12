using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Neura.Web.Hubs;

/// <summary>
/// Authenticated realtime endpoint. Clients are placed into a server-derived
/// per-user group; only Admin/Auditor principals also receive the operations
/// stream. Clients cannot invoke a method to join arbitrary groups.
/// </summary>
[Authorize]
public sealed class NeuralHub : Hub
{
    public const string OperationsGroup = "neura-operations";

    public static string UserGroup(Guid userId) => $"neura-user-{userId:N}";

    public override async Task OnConnectedAsync()
    {
        if (Guid.TryParse(Context.UserIdentifier, out var userId))
            await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(userId));

        if (Context.User?.IsInRole("Admin") == true || Context.User?.IsInRole("Auditor") == true)
            await Groups.AddToGroupAsync(Context.ConnectionId, OperationsGroup);

        await base.OnConnectedAsync();
    }
}
