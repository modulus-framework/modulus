namespace Modulus.Data.MongoDB;

using global::MongoDB.Bson;
using global::MongoDB.Bson.Serialization;
using global::MongoDB.Driver;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Entities;

/// <summary>
/// A collection of tenant documents that cannot cross companies. Every filter-taking operation (find, count,
/// update, replace, delete, find-one-and-*, aggregate, bulk write) is AND-ed with <see cref="MongoTenantFilter"/>;
/// inserts and replacements are stamped with the ambient tenant and rejected when they carry another one; an
/// update that sets, unsets or renames the tenant field is rejected. MongoDB has no row-level security, so this
/// wrapper is the enforcement point: hand modules a <see cref="TenantScopedCollection{T}"/>
/// (<c>ModuleMongoContext.GetTenantCollection</c>), never the raw <see cref="IMongoCollection{T}"/>.
/// </summary>
/// <remarks>
/// The host context (<c>ICurrentTenant.Change(null)</c>) sees and writes every tenant; no tenant in scope matches
/// nothing and cannot insert. <see cref="Unscoped"/> is the explicit, reasoned escape hatch.
/// </remarks>
/// <typeparam name="T">The document type.</typeparam>
public sealed class TenantScopedCollection<T>
    where T : IHasTenantId
{
    private readonly IMongoCollection<T> _inner;
    private readonly ICurrentTenant _tenant;
    private readonly ISecurityAuditLog? _audit;
    private readonly string _tenantField;

    /// <summary>Wraps <paramref name="inner"/> for the ambient tenant of <paramref name="tenant"/>.</summary>
    /// <param name="inner">The raw collection.</param>
    /// <param name="tenant">The ambient tenant, read on every call.</param>
    /// <param name="audit">Records each <see cref="Unscoped"/> use in the security audit; null records nothing.</param>
    public TenantScopedCollection(IMongoCollection<T> inner, ICurrentTenant tenant, ISecurityAuditLog? audit = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _tenant = tenant ?? throw new ArgumentNullException(nameof(tenant));
        _audit = audit;
        _tenantField = inner.DocumentSerializer is IBsonDocumentSerializer serializer
            && serializer.TryGetMemberSerializationInfo(nameof(IHasTenantId.TenantId), out var info)
                ? info.ElementName
                : nameof(IHasTenantId.TenantId);
    }

    /// <summary>The collection name.</summary>
    public string CollectionName => _inner.CollectionNamespace.CollectionName;

    /// <summary>The ambient tenant's filter AND-ed with <paramref name="filter"/>.</summary>
    /// <param name="filter">The caller's filter, or null for all of the tenant's documents.</param>
    public FilterDefinition<T> Scope(FilterDefinition<T>? filter = null)
        => filter is null ? MongoTenantFilter.For<T>(_tenant) : MongoTenantFilter.And(_tenant, filter);

    /// <summary>
    /// The raw collection, outside tenant enforcement. For reviewed host or maintenance work only (index creation,
    /// migrations); the reason documents the decision at the call site, and every call is recorded in the security
    /// audit (<c>mongo.unscoped</c>, outcome <c>overridden</c>) when the collection was given an audit log.
    /// </summary>
    /// <param name="reason">Why the tenant scope must be bypassed.</param>
    public IMongoCollection<T> Unscoped(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        _audit?.Record(new SecurityAuditEvent
        {
            Category = SecurityAuditCategories.Data,
            Action = "mongo.unscoped",
            Outcome = SecurityAuditOutcomes.Overridden,
            TenantId = _tenant.TenantId,
            Target = CollectionName,
            Details = new Dictionary<string, string?> { ["reason"] = reason },
        });
        return _inner;
    }

    /// <summary>Finds the tenant's documents matching <paramref name="filter"/>.</summary>
    /// <param name="filter">The filter.</param>
    /// <param name="options">Find options.</param>
    public IFindFluent<T, T> Find(FilterDefinition<T> filter, FindOptions? options = null)
        => _inner.Find(Scope(filter), options);

    /// <summary>Counts the tenant's documents matching <paramref name="filter"/>.</summary>
    /// <param name="filter">The filter.</param>
    /// <param name="options">Count options.</param>
    /// <param name="ct">Cancellation.</param>
    public Task<long> CountDocumentsAsync(FilterDefinition<T> filter, CountOptions? options = null, CancellationToken ct = default)
        => _inner.CountDocumentsAsync(Scope(filter), options, ct);

    /// <summary>Inserts a document stamped with the ambient tenant.</summary>
    /// <param name="document">The document; a foreign tenant id is rejected.</param>
    /// <param name="options">Insert options.</param>
    /// <param name="ct">Cancellation.</param>
    public Task InsertOneAsync(T document, InsertOneOptions? options = null, CancellationToken ct = default)
    {
        MongoTenantGuard.Stamp(document, _tenant);
        return _inner.InsertOneAsync(document, options, ct);
    }

    /// <summary>Inserts documents stamped with the ambient tenant.</summary>
    /// <param name="documents">The documents; a foreign tenant id is rejected before anything is written.</param>
    /// <param name="options">Insert options.</param>
    /// <param name="ct">Cancellation.</param>
    public Task InsertManyAsync(IEnumerable<T> documents, InsertManyOptions? options = null, CancellationToken ct = default)
    {
        var list = documents?.ToList() ?? throw new ArgumentNullException(nameof(documents));
        foreach (var document in list)
            MongoTenantGuard.Stamp(document, _tenant);
        return _inner.InsertManyAsync(list, options, ct);
    }

    /// <summary>Replaces one of the tenant's documents; the replacement is stamped and checked.</summary>
    /// <param name="filter">The filter.</param>
    /// <param name="replacement">The new document.</param>
    /// <param name="options">Replace options.</param>
    /// <param name="ct">Cancellation.</param>
    public Task<ReplaceOneResult> ReplaceOneAsync(
        FilterDefinition<T> filter, T replacement, ReplaceOptions? options = null, CancellationToken ct = default)
    {
        MongoTenantGuard.Stamp(replacement, _tenant);
        return _inner.ReplaceOneAsync(Scope(filter), replacement, options, ct);
    }

    /// <summary>Updates one of the tenant's documents; an update touching the tenant field is rejected.</summary>
    /// <param name="filter">The filter.</param>
    /// <param name="update">The update.</param>
    /// <param name="options">Update options.</param>
    /// <param name="ct">Cancellation.</param>
    public Task<UpdateResult> UpdateOneAsync(
        FilterDefinition<T> filter, UpdateDefinition<T> update, UpdateOptions? options = null, CancellationToken ct = default)
    {
        Check(update);
        return _inner.UpdateOneAsync(Scope(filter), update, options, ct);
    }

    /// <summary>Updates the tenant's documents; an update touching the tenant field is rejected.</summary>
    /// <param name="filter">The filter.</param>
    /// <param name="update">The update.</param>
    /// <param name="options">Update options.</param>
    /// <param name="ct">Cancellation.</param>
    public Task<UpdateResult> UpdateManyAsync(
        FilterDefinition<T> filter, UpdateDefinition<T> update, UpdateOptions? options = null, CancellationToken ct = default)
    {
        Check(update);
        return _inner.UpdateManyAsync(Scope(filter), update, options, ct);
    }

    /// <summary>Deletes one of the tenant's documents.</summary>
    /// <param name="filter">The filter.</param>
    /// <param name="ct">Cancellation.</param>
    public Task<DeleteResult> DeleteOneAsync(FilterDefinition<T> filter, CancellationToken ct = default)
        => _inner.DeleteOneAsync(Scope(filter), ct);

    /// <summary>Deletes the tenant's documents matching <paramref name="filter"/>.</summary>
    /// <param name="filter">The filter.</param>
    /// <param name="ct">Cancellation.</param>
    public Task<DeleteResult> DeleteManyAsync(FilterDefinition<T> filter, CancellationToken ct = default)
        => _inner.DeleteManyAsync(Scope(filter), ct);

    /// <summary>Atomically updates and returns one of the tenant's documents.</summary>
    /// <param name="filter">The filter.</param>
    /// <param name="update">The update; touching the tenant field is rejected.</param>
    /// <param name="options">Options.</param>
    /// <param name="ct">Cancellation.</param>
    public Task<T> FindOneAndUpdateAsync(
        FilterDefinition<T> filter, UpdateDefinition<T> update, FindOneAndUpdateOptions<T, T>? options = null,
        CancellationToken ct = default)
    {
        Check(update);
        return _inner.FindOneAndUpdateAsync(Scope(filter), update, options, ct);
    }

    /// <summary>Atomically deletes and returns one of the tenant's documents.</summary>
    /// <param name="filter">The filter.</param>
    /// <param name="options">Options.</param>
    /// <param name="ct">Cancellation.</param>
    public Task<T> FindOneAndDeleteAsync(
        FilterDefinition<T> filter, FindOneAndDeleteOptions<T, T>? options = null, CancellationToken ct = default)
        => _inner.FindOneAndDeleteAsync(Scope(filter), options, ct);

    /// <summary>
    /// An aggregation pipeline that starts with a <c>$match</c> on the tenant, so every later stage sees only the
    /// tenant's documents. A <c>$lookup</c> into another tenant collection is not filtered: add the tenant to
    /// its pipeline.
    /// </summary>
    /// <param name="options">Aggregate options.</param>
    public IAggregateFluent<T> Aggregate(AggregateOptions? options = null)
        => _inner.Aggregate(options).Match(Scope());

    /// <summary>Runs a bulk write with every model scoped, stamped or checked like the single operations.</summary>
    /// <param name="requests">The write models (insert, replace, update, delete).</param>
    /// <param name="options">Bulk write options.</param>
    /// <param name="ct">Cancellation.</param>
    public Task<BulkWriteResult<T>> BulkWriteAsync(
        IEnumerable<WriteModel<T>> requests, BulkWriteOptions? options = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(requests);
        var scoped = requests.Select(ScopeModel).ToList();
        return _inner.BulkWriteAsync(scoped, options, ct);
    }

    private WriteModel<T> ScopeModel(WriteModel<T> model)
    {
        switch (model)
        {
            case InsertOneModel<T> insert:
                MongoTenantGuard.Stamp(insert.Document, _tenant);
                return insert;
            case ReplaceOneModel<T> replace:
                MongoTenantGuard.Stamp(replace.Replacement, _tenant);
                return new ReplaceOneModel<T>(Scope(replace.Filter), replace.Replacement)
                {
                    Collation = replace.Collation,
                    Hint = replace.Hint,
                    IsUpsert = replace.IsUpsert,
                };
            case UpdateOneModel<T> one:
                Check(one.Update);
                return new UpdateOneModel<T>(Scope(one.Filter), one.Update)
                {
                    ArrayFilters = one.ArrayFilters,
                    Collation = one.Collation,
                    Hint = one.Hint,
                    IsUpsert = one.IsUpsert,
                };
            case UpdateManyModel<T> many:
                Check(many.Update);
                return new UpdateManyModel<T>(Scope(many.Filter), many.Update)
                {
                    ArrayFilters = many.ArrayFilters,
                    Collation = many.Collation,
                    Hint = many.Hint,
                    IsUpsert = many.IsUpsert,
                };
            case DeleteOneModel<T> deleteOne:
                return new DeleteOneModel<T>(Scope(deleteOne.Filter)) { Collation = deleteOne.Collation, Hint = deleteOne.Hint };
            case DeleteManyModel<T> deleteMany:
                return new DeleteManyModel<T>(Scope(deleteMany.Filter)) { Collation = deleteMany.Collation, Hint = deleteMany.Hint };
            default:
                throw new NotSupportedException($"{model.GetType().Name} is not supported by TenantScopedCollection.");
        }
    }

    private void Check(UpdateDefinition<T> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (_tenant.IsHost)
            return;
        var rendered = update.Render(new RenderArgs<T>(_inner.DocumentSerializer, _inner.Settings.SerializerRegistry));
        if (TouchesTenantField(rendered))
            throw new InvalidOperationException(
                $"An update of {CollectionName} may not change the tenant field '{_tenantField}' outside the host context.");
    }

    private bool TouchesTenantField(BsonValue rendered)
    {
        if (rendered is BsonArray pipeline)
            return pipeline.OfType<BsonDocument>().Any(stage => stage.Names.Any(n => n is "$replaceRoot" or "$replaceWith")
                || TouchesTenantField(stage));

        if (rendered is not BsonDocument document)
            return false;
        foreach (var element in document)
        {
            if (element.Value is not BsonDocument fields)
                continue;
            foreach (var field in fields)
            {
                if (IsTenantPath(field.Name)
                    || (element.Name == "$rename" && field.Value is BsonString target && IsTenantPath(target.Value)))
                    return true;
            }
        }

        return false;
    }

    private bool IsTenantPath(string path)
        => path == _tenantField || path.StartsWith(_tenantField + ".", StringComparison.Ordinal);
}

/// <summary>Stamps and checks the tenant of a document about to be written.</summary>
internal static class MongoTenantGuard
{
    public static void Stamp<T>(T document, ICurrentTenant tenant)
        where T : IHasTenantId
    {
        ArgumentNullException.ThrowIfNull(document);
        if (tenant.IsHost)
            return;
        if (tenant.TenantId is not { } current)
            throw new CrossTenantWriteException(typeof(T).Name, document.TenantId, null);
        if (document.TenantId == Guid.Empty)
            document.TenantId = current;
        else if (document.TenantId != current)
            throw new CrossTenantWriteException(typeof(T).Name, document.TenantId, current);
    }
}
