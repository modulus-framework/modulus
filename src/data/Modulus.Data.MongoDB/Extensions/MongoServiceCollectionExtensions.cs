namespace Modulus.Data.MongoDB.Extensions;

using global::MongoDB.Driver;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;

public static class MongoServiceCollectionExtensions
{
    public static IServiceCollection AddMongoDatabase(
        this IServiceCollection services,
        Action<MongoOptions> configure)
    {
        var opts = new MongoOptions();
        configure(opts);
        services.AddSingleton(Options.Create(opts));

        services.AddSingleton<IMongoClient>(_ =>
            new MongoClient(opts.ConnectionString));

        services.AddSingleton<IMongoDatabase>(sp =>
            sp.GetRequiredService<IMongoClient>()
              .GetDatabase(opts.DatabaseName));

        services.TryAddScoped<ITenantMongoDatabase, SharedTenantMongoDatabase>();

        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IModuleHealthCheck, MongoHealthCheck>());

        return services;
    }

    /// <summary>
    /// Registers MongoDB with one database per tenant: module contexts that take <see cref="ITenantMongoDatabase"/> get
    /// <paramref name="tenantDatabaseName"/>(tenant) for a company and <see cref="MongoOptions.DatabaseName"/> in the host
    /// context, and no tenant at all throws (it never falls back to a shared database). <see cref="IMongoDatabase"/> stays
    /// the host database for infrastructure (outbox, inbox, health). Wrapped collections
    /// (<see cref="TenantScopedCollection{T}"/>) still filter and stamp inside each database.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">The connection, the host database name and the collection prefix.</param>
    /// <param name="tenantDatabaseName">The database of a tenant, e.g. <c>id =&gt; $"shop_{id:N}"</c>; never the host database.</param>
    public static IServiceCollection AddMongoDatabasePerTenant(
        this IServiceCollection services,
        Action<MongoOptions> configure,
        Func<Guid, string> tenantDatabaseName)
    {
        ArgumentNullException.ThrowIfNull(configure);
        ArgumentNullException.ThrowIfNull(tenantDatabaseName);

        string? hostDatabase = null;
        services.AddMongoDatabase(o =>
        {
            configure(o);
            hostDatabase = o.DatabaseName;
        });
        ArgumentException.ThrowIfNullOrWhiteSpace(hostDatabase, nameof(MongoOptions.DatabaseName));

        services.Replace(ServiceDescriptor.Scoped<ITenantMongoDatabase>(sp => new PerTenantMongoDatabase(
            sp.GetRequiredService<IMongoClient>(), sp.GetRequiredService<ICurrentTenant>(), hostDatabase, tenantDatabaseName)));
        return services;
    }
}
