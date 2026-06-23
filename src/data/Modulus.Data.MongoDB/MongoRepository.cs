namespace Modulus.Data.MongoDB;

using global::MongoDB.Driver;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Domain;
using Modulus.Core.Abstractions.Entities;
using Modulus.Data.Abstractions;

/// <summary>
/// Generic MongoDB repository.
/// T  = domain entity (AggregateRoot)
/// TDoc = MongoDB document (BSON-attributed class)
/// </summary>
public abstract class MongoRepository<T, TDoc>(
    IMongoCollection<TDoc> collection,
    ICurrentTenant         tenant)
    : IRepository<T>
    where T    : AggregateRoot
    where TDoc : class, IHasTenantId
{
    protected readonly IMongoCollection<TDoc> Collection = collection;
    protected readonly ICurrentTenant         Tenant     = tenant;

    protected FilterDefinition<TDoc> TenantFilter
        => MongoTenantFilter.For<TDoc>(Tenant);

    // Subclass implements these two mapping methods
    protected abstract T    ToDomain(TDoc doc);
    protected abstract TDoc ToDocument(T entity);

    public async Task<T?> GetByIdAsync(object id, CancellationToken ct)
    {
        var guid   = (Guid)id;
        var filter = MongoTenantFilter.And<TDoc>(Tenant,
            Builders<TDoc>.Filter.Eq("_id", guid));
        var doc = await Collection.Find(filter).FirstOrDefaultAsync(ct);
        return doc is null ? null : ToDomain(doc);
    }

    public async Task<IReadOnlyList<T>> ListAsync(
        ISpecification<T> spec, CancellationToken ct)
    {
        // Basic spec support: filter applied client-side for now
        // Production: translate spec.Filter to MongoDB FilterDefinition
        var docs = await Collection
            .Find(TenantFilter)
            .ToListAsync(ct);
        IEnumerable<T> domain = docs.Select(ToDomain);
        if (spec.Filter     != null) domain = domain.Where(spec.Filter.Compile());
        if (spec.OrderBy    != null) domain = domain.OrderBy(spec.OrderBy.Compile());
        if (spec.OrderByDesc!= null) domain = domain.OrderByDescending(spec.OrderByDesc.Compile());
        if (spec.Skip       != null) domain = domain.Skip(spec.Skip.Value);
        if (spec.Take       != null) domain = domain.Take(spec.Take.Value);
        return domain.ToList();
    }

    public Task<int> CountAsync(ISpecification<T> spec, CancellationToken ct)
        => Collection.CountDocumentsAsync(TenantFilter, null, ct)
                     .ContinueWith(t => (int)t.Result, ct);

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