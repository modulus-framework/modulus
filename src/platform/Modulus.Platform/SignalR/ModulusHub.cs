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

    protected ModulusHub(ICurrentUser currentUser)
    {
        CurrentUser = currentUser;
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

    protected Task LeaveGroupAsync(string groupName)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);

    /// <summary>Standard group name helper: <c>{prefix}:{id}</c></summary>
    protected static string GroupName(string prefix, object id)
        => $"{prefix}:{id}";
}
