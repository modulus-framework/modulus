namespace Modulus.Data.MongoDB;

using global::MongoDB.Driver;
using Microsoft.Extensions.Options;

/// <summary>
/// Base context for MongoDB modules.
/// Provides collection access with automatic prefix.
/// Call EnsureIndexesAsync() from module InitializeAsync.
/// </summary>
public abstract class ModuleMongoContext
{
    protected readonly IMongoDatabase Database;
    protected readonly string         Prefix;

    protected ModuleMongoContext(
        IMongoDatabase           database,
        IOptions<MongoOptions>   opts)
    {
        Database = database;
        Prefix   = opts.Value.CollectionPrefix;
    }

    /// <summary>
    /// Get a typed collection with automatic prefix.
    /// </summary>
    protected IMongoCollection<T> GetCollection<T>(string name)
        => Database.GetCollection<T>(Prefix + name);

    /// <summary>
    /// Override to create indexes. Called from module InitializeAsync.
    /// </summary>
    public virtual Task EnsureIndexesAsync(
        CancellationToken ct = default)
        => Task.CompletedTask;
}