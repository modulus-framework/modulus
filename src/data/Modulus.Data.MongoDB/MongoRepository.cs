namespace Modulus.Data.MongoDB;

using global::MongoDB.Driver;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Domain;
using Modulus.Core.Abstractions.Entities;
using Modulus.Data.Abstractions;

/// <summary>
/// Generic MongoDB repository.
/// <typeparamref name="T"/> = domain entity (AggregateRoot)
/// <typeparamref name="TDoc"/> = MongoDB document (BSON-attributed class)
///
/// Override <see cref="BuildDocumentFilter"/> to translate domain-level
/// <c>ISpecification&lt;T&gt;</c> filters into server-side
/// <c>FilterDefinition&lt;TDoc&gt;</c> for efficient queries.
/// When not overridden, filter and sort are applied client-side after
/// fetching all matching tenant documents.
/// </summary>
public abstract class MongoRepository<T, TDoc>(
    IMongoCollection<TDoc> collection,
    ICurrentTenant tenant)
    : IRepository<T>
    where T : AggregateRoot
    where TDoc : class, IHasTenantId
{
    protected readonly IMongoCollection<TDoc> Collection = collection;
    protected readonly ICurrentTenant Tenant = tenant;

    protected FilterDefinition<TDoc> TenantFilter
        => MongoTenantFilter.For<TDoc>(Tenant);

    // Subclass implements these two mapping methods
    protected abstract T ToDomain(TDoc doc);
    protected abstract TDoc ToDocument(T entity);

    /// <summary>
    /// Override to translate a domain spec filter into a MongoDB
    /// <see cref="FilterDefinition{TDoc}"/>. Returning <c>null</c> (default)
    /// falls back to client-side filtering after fetching all tenant docs.
    /// </summary>
    protected virtual FilterDefinition<TDoc>? BuildDocumentFilter(
        ISpecification<T> spec) => null;

    /// <summary>
    /// Override to translate a domain spec ordering into a
    /// <see cref="SortDefinition{TDoc}"/>. Returning <c>null</c> (default)
    /// falls back to client-side ordering.
    /// </summary>
    protected virtual SortDefinition<TDoc>? BuildDocumentSort(
        ISpecification<T> spec) => null;

    public async Task<T?> GetByIdAsync(object id, CancellationToken ct)
    {
        // Accept both Guid and string ids.
        FilterDefinition<TDoc> idFilter;
        if (id is Guid g)
            idFilter = Builders<TDoc>.Filter.Eq("_id", g);
        else
            idFilter = Builders<TDoc>.Filter.Eq("_id", id);

        var filter = MongoTenantFilter.And<TDoc>(Tenant, idFilter);
        var doc = await Collection.Find(filter).FirstOrDefaultAsync(ct);
        return doc is null ? null : ToDomain(doc);
    }

    public async Task<IReadOnlyList<T>> ListAsync(
        ISpecification<T> spec, CancellationToken ct)
    {
        var docFilter = BuildDocumentFilter(spec);
        var serverFilter = docFilter is not null
            ? Builders<TDoc>.Filter.And(TenantFilter, docFilter)
            : TenantFilter;

        var docSort = BuildDocumentSort(spec);

        var findFluent = Collection.Find(serverFilter);
        if (docSort is not null)
            findFluent = findFluent.Sort(docSort);

        // Apply server-side pagination when a server-side filter is available
        // (so we know the ordering is stable). Fall back to client-side pagination
        // when only the tenant filter is applied and the domain ordering may differ.
        if (docFilter is not null)
        {
            if (spec.Skip.HasValue) findFluent = findFluent.Skip(spec.Skip.Value);
            if (spec.Take.HasValue) findFluent = findFluent.Limit(spec.Take.Value);
            var docs = await findFluent.ToListAsync(ct);
            return docs.Select(ToDomain).ToList();
        }

        // Client-side fallback: fetch all tenant docs, map, then filter/sort/page.
        IEnumerable<T> domain = (await findFluent.ToListAsync(ct)).Select(ToDomain);
        if (spec.Filter     is not null) domain = domain.Where(spec.Filter.Compile());
        if (spec.OrderBy    is not null) domain = domain.OrderBy(spec.OrderBy.Compile());
        if (spec.OrderByDesc is not null) domain = domain.OrderByDescending(spec.OrderByDesc.Compile());
        if (spec.Skip.HasValue)  domain = domain.Skip(spec.Skip.Value);
        if (spec.Take.HasValue)  domain = domain.Take(spec.Take.Value);
        return domain.ToList();
    }

    public async Task<int> CountAsync(ISpecification<T> spec, CancellationToken ct)
    {
        var docFilter = BuildDocumentFilter(spec);
        if (docFilter is not null)
        {
            // Server-side count with translated filter — fast path.
            var serverFilter = Builders<TDoc>.Filter.And(TenantFilter, docFilter);
            return (int)await Collection.CountDocumentsAsync(serverFilter, null, ct);
        }

        if (spec.Filter is null)
        {
            // No domain filter → count all tenant documents server-side.
            return (int)await Collection.CountDocumentsAsync(TenantFilter, null, ct);
        }

        // Domain filter with no server-side translation — fetch and count client-side.
        var docs = await Collection.Find(TenantFilter).ToListAsync(ct);
        var compiled = spec.Filter.Compile();
        return docs.Select(ToDomain).Count(compiled);
    }

    public Task AddAsync(T entity, CancellationToken ct)
        => Collection.InsertOneAsync(ToDocument(entity), null, ct);

    public async Task AddRangeAsync(IEnumerable<T> entities, CancellationToken ct)
        => await Collection.InsertManyAsync(
            entities.Select(ToDocument), null, ct);

    public Task UpdateAsync(T entity, CancellationToken ct)
        => Collection.ReplaceOneAsync(
            MongoTenantFilter.And<TDoc>(Tenant,
                Builders<TDoc>.Filter.Eq("_id", entity.Id)),
            ToDocument(entity),
            new ReplaceOptions { IsUpsert = false }, ct);

    public Task DeleteAsync(T entity, CancellationToken ct)
        => Collection.DeleteOneAsync(
            MongoTenantFilter.And<TDoc>(Tenant,
                Builders<TDoc>.Filter.Eq("_id", entity.Id)), ct);
}
