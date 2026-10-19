namespace Modulus.Authorization;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Modulus.Core.Abstractions;

/// <summary>
/// Applies all deferred <see cref="IPermissionRegistration"/> entries to the
/// real <see cref="IPermissionRegistry"/> singleton, then freezes it so the
/// runtime sees an immutable permission set. When a <see cref="IPermissionCatalogSnapshotStore"/> is registered, it also
/// logs the lifecycle and replacement changes since the last startup and records the current catalog.
/// </summary>
internal sealed partial class PermissionInitHostedService(
    IPermissionRegistry registry,
    IEnumerable<IPermissionRegistration> registrations,
    IServiceScopeFactory scopes,
    ILogger<PermissionInitHostedService> logger)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        foreach (var registration in registrations)
            registration.Apply(registry);
        registry.Freeze();
        await NoticeCatalogChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    private async Task NoticeCatalogChangesAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var store = scope.ServiceProvider.GetService<IPermissionCatalogSnapshotStore>();
        if (store is null)
            return;

        try
        {
            var declared = registry.GetAll();
            var recorded = await store.LoadAsync(ct).ConfigureAwait(false);
            foreach (var change in PermissionCatalogChanges.Compare(recorded, declared))
                LogCatalogChange(logger, change.Permission, change.Kind.ToString(), change.Before, change.After);

            await store.SaveAsync(declared, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Startup is not blocked by the notice: the catalog is still in force, only the comparison is missed.
            LogSnapshotFailed(logger, ex);
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "Permission catalog change: {Permission} {Kind} from {Before} to {After}. Review the grants that name it.")]
    private static partial void LogCatalogChange(ILogger logger, string permission, string kind, string before, string after);

    [LoggerMessage(EventId = 2, Level = LogLevel.Error,
        Message = "The permission catalog snapshot could not be read or saved; catalog changes were not checked for this startup.")]
    private static partial void LogSnapshotFailed(ILogger logger, Exception exception);
}
