namespace Modulus.EntityFrameworkCore.Isolation;

/// <summary>
/// Lets a provisioning run migrate a tenant database before its store is active. The resolver serves only active stores, so
/// while a store is being set up its connection string is given to the context directly, for this execution flow only. The
/// override applies to one tenant and one store key and to contexts of the same provider; nothing else reads it.
/// </summary>
/// <remarks>
/// Only provisioning code sets the override, inside its own scope. A request never sets it, so a request cannot reach a
/// database through it.
/// </remarks>
public static class TenantDatabaseSetup
{
    private static readonly AsyncLocal<SetupTarget?> s_current = new();

    /// <summary>Overrides the connection of the tenant's store until the returned scope is disposed.</summary>
    public static IDisposable Use(Guid tenantId, string storeKey, string providerKey, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storeKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var previous = s_current.Value;
        s_current.Value = new SetupTarget(tenantId, storeKey, providerKey, connectionString);
        return new Restore(previous);
    }

    internal static bool TryGet(Guid tenantId, string storeKey, string providerKey, out string? connectionString)
    {
        var current = s_current.Value;
        if (current is not null
            && current.TenantId == tenantId
            && string.Equals(current.StoreKey, storeKey, StringComparison.Ordinal)
            && string.Equals(current.ProviderKey, providerKey, StringComparison.Ordinal))
        {
            connectionString = current.ConnectionString;
            return true;
        }

        connectionString = null;
        return false;
    }

    private sealed record SetupTarget(Guid TenantId, string StoreKey, string ProviderKey, string ConnectionString);

    private sealed class Restore(SetupTarget? previous) : IDisposable
    {
        public void Dispose() => s_current.Value = previous;
    }
}
