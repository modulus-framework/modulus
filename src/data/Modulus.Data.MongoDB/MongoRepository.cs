namespace Modulus.Data.MongoDB;

using global::MongoDB.Bson;
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

    /// <summary>
    /// The most documents one client-side query may load before it is refused. A query that falls back to client-side
    /// filtering or sorting reads every matching document, so this bounds memory instead of letting a large collection load whole.
    /// </summary>
    protected virtual int MaxClientSideScan => 10_000;

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
        // Server-side paging is only correct when the order is applied on the server too: a sort the repository could not
        // translate must be applied before Skip/Take, so that case pages client-side below.
        var sortedOnServer = docSort is not null || spec.OrderByClauses is not { Count: > 0 };
        if (docFilter is not null && sortedOnServer)
        {
            if (spec.Skip.HasValue) findFluent = findFluent.Skip(spec.Skip.Value);
            if (spec.Take.HasValue) findFluent = findFluent.Limit(spec.Take.Value);
            var docs = await findFluent.ToListAsync(ct);
            return docs.Select(ToDomain).ToList();
        }

        // Client-side fallback: fetch the matching tenant docs (bounded by MaxClientSideScan), map, then filter/sort/page.
        var scanned = await findFluent.ToListAsync(ct);
        if (scanned.Count > MaxClientSideScan)
        {
            throw new InvalidOperationException(
                $"The query for {typeof(T).Name} matched {scanned.Count} documents, more than MaxClientSideScan ({MaxClientSideScan}). " +
                "Translate the filter and sort with BuildDocumentFilter/BuildDocumentSort, or raise MaxClientSideScan.");
        }
        IEnumerable<T> domain = scanned.Select(ToDomain);
        if (spec.Filter is not null) domain = domain.Where(spec.Filter.Compile());

        // Apply ordering: OrderBy, then ThenBy for each subsequent clause
        if (spec.OrderByClauses is { Count: > 0 })
        {
            var orderedDomain = (IOrderedEnumerable<T>?)null;
            foreach (var orderBy in spec.OrderByClauses)
            {
                var compiledSelector = orderBy.Selector.Compile();
                orderedDomain = orderedDomain is null
                    ? (orderBy.Descending
                        ? domain.OrderByDescending(compiledSelector)
                        : domain.OrderBy(compiledSelector))
                    : (orderBy.Descending
                        ? orderedDomain.ThenByDescending(compiledSelector)
                        : orderedDomain.ThenBy(compiledSelector));
            }
            domain = orderedDomain ?? domain;
        }

        if (spec.Skip.HasValue) domain = domain.Skip(spec.Skip.Value);
        if (spec.Take.HasValue) domain = domain.Take(spec.Take.Value);
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

    // Writes stamp the ambient tenant on the document and reject one carrying another tenant, the
    // same rule ModuleDbContext applies on SaveChanges.
    public Task AddAsync(T entity, CancellationToken ct)
        => Collection.InsertOneAsync(Stamped(entity), null, ct);

    public async Task AddRangeAsync(IEnumerable<T> entities, CancellationToken ct)
        => await Collection.InsertManyAsync(
            entities.Select(Stamped).ToList(), null, ct);

    public Task UpdateAsync(T entity, CancellationToken ct)
        => Collection.ReplaceOneAsync(
            MongoTenantFilter.And<TDoc>(Tenant,
                Builders<TDoc>.Filter.Eq("_id", entity.Id)),
            Stamped(entity),
            new ReplaceOptions { IsUpsert = false }, ct);

    private TDoc Stamped(T entity)
    {
        var document = ToDocument(entity);
        MongoTenantGuard.Stamp(document, Tenant);
        return document;
    }

    public Task DeleteAsync(T entity, CancellationToken ct)
        => Collection.DeleteOneAsync(
            MongoTenantFilter.And<TDoc>(Tenant,
                Builders<TDoc>.Filter.Eq("_id", entity.Id)), ct);

    public async Task<T?> FirstOrDefaultAsync(ISpecification<T> spec, CancellationToken ct)
    {
        var list = await ListAsync(spec, ct);
        return list.FirstOrDefault();
    }

    public async Task<T> SingleAsync(ISpecification<T> spec, CancellationToken ct)
    {
        var list = await ListAsync(spec, ct);
        return list.Count == 1
            ? list[0]
            : throw new InvalidOperationException(
                $"Expected exactly one entity, but found {list.Count}.");
    }

    public async Task<T?> SingleOrDefaultAsync(ISpecification<T> spec, CancellationToken ct)
    {
        var list = await ListAsync(spec, ct);
        return list.Count switch
        {
            0 => null,
            1 => list[0],
            _ => throw new InvalidOperationException(
                $"Expected at most one entity, but found {list.Count}.")
        };
    }

    public async IAsyncEnumerable<T> AsAsyncEnumerable(ISpecification<T> spec)
    {
        var items = await ListAsync(spec, CancellationToken.None);
        foreach (var item in items)
            yield return item;
    }

    public async Task DeleteRangeAsync(ISpecification<T> spec, CancellationToken ct)
    {
        // A page cannot be deleted by filter: DeleteMany ignores Skip/Take and would take every match. Resolve the ids of
        // exactly that page (ListAsync applies the sort, Skip and Take) and delete those.
        if (spec.Skip.HasValue || spec.Take.HasValue)
        {
            await DeleteIdsAsync(spec, ct);
            return;
        }

        var docFilter = BuildDocumentFilter(spec);
        if (docFilter is not null)
        {
            // Server-side delete, always tenant-scoped.
            await Collection.DeleteManyAsync(
                Builders<TDoc>.Filter.And(TenantFilter, docFilter), ct);
            return;
        }

        if (spec.Filter is null)
        {
            // No domain filter: delete everything in the current tenant scope.
            await Collection.DeleteManyAsync(TenantFilter, ct);
            return;
        }

        // Domain filter without a server-side translation: resolve the
        // matching ids first (ListAsync already applies the tenant filter,
        // domain filter and ordering), then delete exactly those ids —
        // again tenant-scoped. Never falls back to an unscoped DeleteMany.
        await DeleteIdsAsync(spec, ct);
    }

    private async Task DeleteIdsAsync(ISpecification<T> spec, CancellationToken ct)
    {
        var ids = (await ListAsync(spec, ct)).Select(e => e.Id).ToList();
        if (ids.Count == 0)
            return;

        // Guid ids are sent in the Standard representation the documents are stored with; a raw Guid has no serializer here.
        var idFilter = Builders<TDoc>.Filter.In("_id", ids.Select(id => (BsonValue)new BsonBinaryData(id, GuidRepresentation.Standard)));
        await Collection.DeleteManyAsync(
            MongoTenantFilter.And<TDoc>(Tenant, idFilter), ct);
    }
}
