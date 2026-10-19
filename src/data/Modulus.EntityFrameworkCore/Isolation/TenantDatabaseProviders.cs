namespace Modulus.EntityFrameworkCore.Isolation;

using System.Data.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions.Tenancy;

/// <summary>The registered operations of each tenant database provider, keyed by provider key.</summary>
public sealed class TenantDatabaseProviderOptions
{
    private readonly Dictionary<string, Entry> _providers = new(StringComparer.Ordinal);

    /// <summary>Adds (or replaces) a provider's connection factory and, when the provider pools connections, its pool clear.</summary>
    /// <param name="providerKey">The provider key the catalog stores, for example <c>postgresql</c>.</param>
    /// <param name="createConnection">Creates an unopened connection for a connection string.</param>
    /// <param name="clearPool">Clears the pool of a connection string; <c>null</c> when the provider has no pool to clear.</param>
    public void Register(string providerKey, Func<string, DbConnection> createConnection, Action<string>? clearPool = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerKey);
        ArgumentNullException.ThrowIfNull(createConnection);
        _providers[providerKey] = new Entry(createConnection, clearPool);
    }

    internal bool TryGet(string providerKey, out Entry? entry) => _providers.TryGetValue(providerKey, out entry);

    /// <summary>One provider's operations.</summary>
    internal sealed record Entry(Func<string, DbConnection> CreateConnection, Action<string>? ClearPool);
}

/// <summary>Opens a probe connection and clears pools through the providers registered in <see cref="TenantDatabaseProviderOptions"/>.</summary>
internal sealed class TenantDatabaseProviders(IOptions<TenantDatabaseProviderOptions> options) : ITenantDatabaseProviders
{
    /// <inheritdoc />
    public async Task ProbeAsync(string providerKey, string connectionString, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        var entry = Require(providerKey);

        await using var connection = entry.CreateConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1";
        await command.ExecuteScalarAsync(cancellationToken);
    }

    /// <inheritdoc />
    public void ClearPool(string providerKey, string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return;
        if (options.Value.TryGet(providerKey, out var entry))
            entry?.ClearPool?.Invoke(connectionString);
    }

    private TenantDatabaseProviderOptions.Entry Require(string providerKey)
        => options.Value.TryGet(providerKey, out var entry) && entry is not null
            ? entry
            : throw new InvalidOperationException($"No tenant database provider is registered for '{providerKey}'. Register it with AddTenantDatabaseProvider.");
}

/// <summary>Registration of tenant database providers.</summary>
public static class TenantDatabaseProviderExtensions
{
    /// <summary>
    /// Registers a provider's probe and pool clear. The provider packages call this (for example <c>AddPostgreSqlPerTenantDatabase</c>);
    /// the provisioning host needs it to probe a new connection string, and every node needs it to clear a rotated pool.
    /// </summary>
    public static IServiceCollection AddTenantDatabaseProvider(
        this IServiceCollection services,
        string providerKey,
        Func<string, DbConnection> createConnection,
        Action<string>? clearPool = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.Configure<TenantDatabaseProviderOptions>(o => o.Register(providerKey, createConnection, clearPool));
        services.TryAddSingleton<ITenantDatabaseProviders, TenantDatabaseProviders>();
        return services;
    }
}
