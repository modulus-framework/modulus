using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Modulus.SignalR.Abstractions;

namespace Modulus.SignalR.Hubs;

using Microsoft.AspNetCore.SignalR;
using Modulus.Core.Abstractions;

public interface IPresenceClient
{
    Task UserJoined(PresenceUserDto user, string resourceId);
    Task UserLeft(Guid userId, string resourceId);
    Task PresenceList(IReadOnlyList<PresenceUserDto> users);
}

public sealed record PresenceUserDto(
    Guid     UserId,
    string   UserName,
    DateTime JoinedAt);

public sealed class PresenceHub(ICurrentUser user)
    : Hub<IPresenceClient>
{
    public async Task JoinResource(string type, string id)
    {
        var key = ResourceGroup(type, id);
        await Groups.AddToGroupAsync(Context.ConnectionId, key);
        var dto = new PresenceUserDto(
            user.UserId!.Value, user.UserName!, DateTime.UtcNow);
        await Clients.OthersInGroup(key).UserJoined(dto, id);
    }

    public async Task LeaveResource(string type, string id)
    {
        var key = ResourceGroup(type, id);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, key);
        await Clients.Group(key).UserLeft(user.UserId!.Value, id);
    }

    public static string ResourceGroup(string type, string id)
        => $"presence:{type}:{id}";
}

internal sealed class PresenceHubRegistrar : IModuleHub
{
    public void MapHub(IEndpointRouteBuilder app)
        => app.MapHub<PresenceHub>("/hubs/presence");
}