namespace Modulus.Data.MongoDB;

using global::MongoDB.Driver;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Entities;

/// <summary>
/// Base context for MongoDB modules.
/// Provides collection access with automatic prefix.
/// Call EnsureIndexesAsync() from module InitializeAsync.
/// </summary>
public abstract class ModuleMongoContext
{
    protected readonly IMongoDatabase Database;
    protected readonly string Prefix;
    private readonly ICurrentTenant? _tenant;

    protected ModuleMongoContext(
        IMongoDatabase database,
        IOptions<MongoOptions> opts)
    {
        Database = database;
        Prefix = opts.Value.CollectionPrefix;
    }

    /// <summary>
    /// A context that can hand out tenant-enforcing collections (<see cref="GetTenantCollection{T}"/>).
    /// </summary>
    /// <param name="database">The module database.</param>
    /// <param name="opts">Mongo options (collection prefix).</param>
    /// <param name="tenant">The ambient tenant.</param>
    protected ModuleMongoContext(
        IMongoDatabase database,
        IOptions<MongoOptions> opts,
        ICurrentTenant tenant)
        : this(database, opts)
    {
        _tenant = tenant ?? throw new ArgumentNullException(nameof(tenant));
    }

    /// <summary>
    /// Get a typed collection with automatic prefix. For collections of tenant documents use
    /// <see cref="GetTenantCollection{T}"/>: a raw collection does not filter, stamp or check the tenant.
    /// </summary>
    protected IMongoCollection<T> GetCollection<T>(string name)
        => Database.GetCollection<T>(Prefix + name);

    /// <summary>
    /// A prefixed collection of tenant documents that cannot cross companies (<see cref="TenantScopedCollection{T}"/>).
    /// Needs the constructor taking <see cref="ICurrentTenant"/>.
    /// </summary>
    /// <param name="name">The collection name, without the module prefix.</param>
    protected TenantScopedCollection<T> GetTenantCollection<T>(string name)
        where T : IHasTenantId
        => new(GetCollection<T>(name), _tenant ?? throw new InvalidOperationException(
            $"{GetType().Name} must pass ICurrentTenant to the ModuleMongoContext constructor to use tenant collections."));

    /// <summary>
    /// Override to create indexes. Called from module InitializeAsync.
    /// </summary>
    public virtual Task EnsureIndexesAsync(
        CancellationToken ct = default)
        => Task.CompletedTask;
}
