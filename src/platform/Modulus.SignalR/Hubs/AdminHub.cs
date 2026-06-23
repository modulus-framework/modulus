using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Modulus.SignalR.Abstractions;

namespace Modulus.SignalR.Hubs;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

public interface IAdminClient
{
    Task NewOrderAlert(AdminOrderDto order);
    Task ModuleHealthChanged(string module, string status);
}

public sealed record AdminOrderDto(
    Guid    OrderId, Guid CustomerId,
    decimal Amount,  DateTime PlacedAt);

[Authorize(Policy = "identity:user:manage")]
public sealed class AdminHub : Hub<IAdminClient>
{
    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(
            Context.ConnectionId, "admin-dashboard");
        await base.OnConnectedAsync();
    }
}

internal sealed class AdminHubRegistrar : IModuleHub
{
    public void MapHub(IEndpointRouteBuilder app)
        => app.MapHub<AdminHub>("/hubs/admin");
}