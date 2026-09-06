namespace Modulus.SignalR;

using Microsoft.AspNetCore.SignalR;
using Modulus.Core.Abstractions;

/// <summary>
/// Base class for application hubs.  Provides typed-client access,
/// automatic user-group association, and connection lifecycle hooks.
/// </summary>
/// <typeparam name="TClient">The typed client interface.</typeparam>
public abstract class ModulusHub<TClient> : Hub<TClient>
    where TClient : class
{
    /// <summary>
    /// Adds the connection to a user-specific group based on the
    /// current user's ID.  Override OnConnectedAsync and call base
    /// to extend the behaviour.
    /// </summary>
    protected ICurrentUser CurrentUser { get; }
    protected ICurrentTenant CurrentTenant { get; }

    protected ModulusHub(ICurrentUser currentUser, ICurrentTenant currentTenant)
    {
        CurrentUser = currentUser;
        CurrentTenant = currentTenant;
    }

    /// <summary>
    /// Joins a group scoped to the current user's ID.
    /// Clients outside this group cannot receive messages sent to it.
    /// </summary>
    protected Task JoinUserGroupAsync(string prefix = "user")
        => CurrentUser.UserId.HasValue
            ? Groups.AddToGroupAsync(Context.ConnectionId, $"{prefix}:{CurrentUser.UserId.Value}")
            : Task.CompletedTask;

    /// <summary>
    /// Joins a named group.  Use for chat rooms, tenant channels, etc.
    /// </summary>
    protected Task JoinGroupAsync(string groupName)
        => Groups.AddToGroupAsync(Context.ConnectionId, groupName);

    /// <summary>
    /// Joins a group scoped to the current tenant, preventing cross-tenant access.
    /// The group name is prefixed with <c>tenant:{TenantId}:</c> to ensure isolation.
    /// Throws <see cref="InvalidOperationException"/> if no tenant is in scope.
    /// </summary>
    protected Task JoinTenantGroupAsync(string groupName)
    {
        if (!CurrentTenant.IsAvailable || CurrentTenant.TenantId is null)
            throw new InvalidOperationException("No tenant in scope; cannot join tenant-scoped group.");
        return Groups.AddToGroupAsync(Context.ConnectionId, $"tenant:{CurrentTenant.TenantId}:{groupName}");
    }

    protected Task LeaveGroupAsync(string groupName)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);

    /// <summary>Leaves a tenant-scoped group (inverse of <see cref="JoinTenantGroupAsync"/>).</summary>
    protected Task LeaveTenantGroupAsync(string groupName)
    {
        if (!CurrentTenant.IsAvailable || CurrentTenant.TenantId is null)
            throw new InvalidOperationException("No tenant in scope; cannot leave tenant-scoped group.");
        return Groups.RemoveFromGroupAsync(Context.ConnectionId, $"tenant:{CurrentTenant.TenantId}:{groupName}");
    }

    /// <summary>Standard group name helper: <c>{prefix}:{id}</c></summary>
    protected static string GroupName(string prefix, object id)
        => $"{prefix}:{id}";

    /// <summary>Tenant-scoped group name helper: <c>tenant:{tenantId}:{groupName}</c></summary>
    protected static string TenantGroupName(Guid tenantId, string groupName)
        => $"tenant:{tenantId}:{groupName}";
}
