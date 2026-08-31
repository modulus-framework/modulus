namespace Modulus.EntityFrameworkCore;

using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions.Common;
using Modulus.Data.Abstractions;

/// <summary>
/// EF Core implementation of <see cref="IRepository{T}"/>. Resolves the
/// correct <see cref="DbContext"/> for entity <typeparamref name="T"/> via the
/// registration-time <see cref="IEntityContextMap"/>. This is essential in a
/// modular monolith where multiple module DbContexts coexist: a naive
/// <c>GetRequiredService&lt;DbContext&gt;()</c> would return only the
/// last-registered context, breaking repos for all other modules.
/// </summary>
public class EfRepository<T>(IServiceProvider sp)
    : IRepository<T> where T : class
{
    private DbContext? _db;
    private DbSet<T>? _set;

    /// <summary>
    /// The DbContext that owns entity <typeparamref name="T"/>. Resolved via the
    /// registration-time <see cref="IEntityContextMap"/> — exactly the owning
    /// context is instantiated, not every registered context. Falls back to a
    /// runtime scan for contexts registered outside <c>AddModuleDatabase</c>.
    /// </summary>
    protected DbContext Db => _db ??= ResolveDbContext(sp);

    protected DbSet<T> Set => _set ??= Db.Set<T>();

    private static DbContext ResolveDbContext(IServiceProvider sp)
    {
        // Fast path: the registration-time map routes entity T to exactly its
        // owning context, so we resolve only that one context instead of
        // instantiating every registered DbContext to scan its model.
        if (sp.GetService<IEntityContextMap>()?.Resolve(typeof(T)) is { } contextType)
            return (DbContext)sp.GetRequiredService(contextType);

        // Fallback: scan every registered DbContext. Covers contexts registered
        // manually (not through AddModuleDatabase), so they are not in the map.
        // Materialize once — GetServices<T>() returns a deferred DI enumerable;
        // iterating it multiple times resolves new instances each time.
        var contexts = sp.GetServices<DbContext>().ToList();

        foreach (var ctx in contexts)
        {
            if (ctx.Model.FindEntityType(typeof(T)) is not null)
                return ctx;
        }

        // Single-context fallback (common in small apps).
        if (contexts.Count == 1)
            return contexts[0];

        throw new InvalidOperationException(
            $"No DbContext containing entity '{typeof(T).Name}' is registered. " +
            "Call AddModuleDatabase<TContext>() for the module that owns this entity.");
    }

    /// <summary>
    /// Fetches an entity by primary key using a LINQ query (NOT FindAsync)
    /// so that global query filters (tenant isolation, soft-delete) are
    /// applied. <see cref="DbSet{TEntity}.FindAsync(object[])"/> bypasses
    /// query filters entirely, which would leak cross-tenant and
    /// soft-deleted records by known id.
    /// <para>
    /// Composite primary keys are supported: pass the key values positionally
    /// as an <c>object[]</c> in primary-key property order.
    /// </para>
    /// </summary>
    public async Task<T?> GetByIdAsync(object id, CancellationToken ct)
    {
        var entityType = Db.Model.FindEntityType(typeof(T));
        var pk = entityType?.FindPrimaryKey();

        if (pk is null || pk.Properties.Count == 0)
            return await Set.FindAsync([id], ct).AsTask();

        var properties = pk.Properties;
        object[] keyValues;

        if (properties.Count == 1)
        {
            keyValues = [id];
        }
        else
        {
            keyValues = id as object[]
                ?? throw new ArgumentException(
                    $"Entity '{typeof(T).Name}' has a composite primary key with " +
                    $"{properties.Count} parts; pass the key values as object[] " +
                    "in primary-key property order.", nameof(id));

            if (keyValues.Length != properties.Count)
                throw new ArgumentException(
                    $"Entity '{typeof(T).Name}' has a composite primary key with " +
                    $"{properties.Count} parts, but {keyValues.Length} key values were supplied.",
                    nameof(id));
        }

        // e => EF.Property<TKey>(e, "K1") == v1 && ... — EF.Property also
        // covers shadow primary-key properties, which Expression.Property
        // cannot reach.
        var efProperty = typeof(EF).GetMethod(nameof(EF.Property))!;
        var parameter = Expression.Parameter(typeof(T), "e");
        Expression? body = null;
        for (var i = 0; i < properties.Count; i++)
        {
            var property = properties[i];
            var propertyExpr = Expression.Call(
                efProperty.MakeGenericMethod(property.ClrType),
                parameter,
                Expression.Constant(property.Name));
            var converted = Expression.Constant(
                CoerceKeyValue(keyValues[i], property.ClrType), property.ClrType);
            var equals = Expression.Equal(propertyExpr, converted);
            body = body is null ? equals : Expression.AndAlso(body, equals);
        }

        var predicate = Expression.Lambda<Func<T, bool>>(body!, parameter);
        return await Set.FirstOrDefaultAsync(predicate, ct);
    }

    private static object? CoerceKeyValue(object value, Type targetType)
    {
        var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (underlying.IsInstanceOfType(value))
            return value;
        return Convert.ChangeType(value, underlying);
    }

    public async Task<IReadOnlyList<T>> ListAsync(
        ISpecification<T> spec, CancellationToken ct)
        => await ApplySpec(Set.AsQueryable(), spec).ToListAsync(ct);

    public Task<int> CountAsync(
        ISpecification<T> spec, CancellationToken ct)
        => ApplySpec(Set.AsQueryable(), spec).CountAsync(ct);

    public async Task AddAsync(T entity, CancellationToken ct)
        => await Set.AddAsync(entity, ct);

    public async Task AddRangeAsync(
        IEnumerable<T> entities, CancellationToken ct)
        => await Set.AddRangeAsync(entities, ct);

    public Task UpdateAsync(T entity, CancellationToken ct)
    {
        // Mark the entity as modified so EF Core tracks it. Do NOT call
        // SaveChangesAsync here — that flushes ALL pending changes across every
        // tracked entity in the context, violating the unit-of-work pattern.
        // The caller is responsible for committing via IUnitOfWork.CommitAsync.
        if (Db.Entry(entity).State == EntityState.Detached)
            Db.Entry(entity).State = EntityState.Modified;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(T entity, CancellationToken ct)
    {
        Set.Remove(entity);
        return Task.CompletedTask;
    }

    public async Task<T?> FirstOrDefaultAsync(ISpecification<T> spec, CancellationToken ct)
        => await ApplySpec(Set.AsQueryable(), spec).FirstOrDefaultAsync(ct);

    public async Task<T> SingleAsync(ISpecification<T> spec, CancellationToken ct)
        => await ApplySpec(Set.AsQueryable(), spec).SingleAsync(ct);

    public async Task<T?> SingleOrDefaultAsync(ISpecification<T> spec, CancellationToken ct)
        => await ApplySpec(Set.AsQueryable(), spec).SingleOrDefaultAsync(ct);

    public IAsyncEnumerable<T> AsAsyncEnumerable(ISpecification<T> spec)
        => ApplySpec(Set.AsQueryable(), spec).AsAsyncEnumerable();

    public async Task DeleteRangeAsync(ISpecification<T> spec, CancellationToken ct)
    {
        var entitiesToDelete = await ApplySpec(Set.AsQueryable(), spec).ToListAsync(ct);
        Set.RemoveRange(entitiesToDelete);
    }

    protected static IQueryable<T> ApplySpec(
        IQueryable<T> query, ISpecification<T> spec)
    {
        if (spec.Filter != null)
            query = query.Where(spec.Filter);

        if (spec.IgnoreQueryFilters)
            query = query.IgnoreQueryFilters();

        if (spec.AsSplitQuery)
            query = query.AsSplitQuery();

        if (spec.IncludeChains is { Count: > 0 })
            foreach (var include in spec.IncludeChains)
                query = query.Include(include.IncludeExpression);

        // Apply ordering: OrderBy, then ThenBy for each subsequent clause
        bool isFirstOrderBy = true;
        if (spec.OrderByClauses is { Count: > 0 })
        {
            foreach (var orderBy in spec.OrderByClauses)
            {
                query = isFirstOrderBy
                    ? (orderBy.Descending
                        ? query.OrderByDescending(orderBy.Selector)
                        : query.OrderBy(orderBy.Selector))
                    : (orderBy.Descending
                        ? ((IOrderedQueryable<T>)query).ThenByDescending(orderBy.Selector)
                        : ((IOrderedQueryable<T>)query).ThenBy(orderBy.Selector));
                isFirstOrderBy = false;
            }
        }

        // Paging requires ordering
        if ((spec.Skip != null || spec.Take != null) && isFirstOrderBy)
            throw new InvalidOperationException(
                "Specifications with Skip/Take must define at least one OrderBy clause.");

        if (spec.Skip != null)
            query = query.Skip(spec.Skip.Value);
        if (spec.Take != null)
            query = query.Take(spec.Take.Value);

        if (spec.Tag is not null)
            query = query.TagWith(spec.Tag);

        if (spec.AsNoTracking)
            query = query.AsNoTracking();

        return query;
    }
}

public class EfReadRepository<T>(IServiceProvider sp)
    : EfRepository<T>(sp), IReadRepository<T>
    where T : class
{
    public async Task<PagedList<TResult>> ListPagedAsync<TResult>(
        ISpecification<T> spec,
        Expression<Func<T, TResult>> selector,
        int page, int size,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(selector);
        var baseQuery = ApplySpec(Set.AsQueryable(), spec);

        // If the spec already has Skip/Take, don't double-apply paging.
        var query = spec.Skip is null && spec.Take is null
            ? baseQuery.Skip((page - 1) * size).Take(size)
            : baseQuery;

        var total = await baseQuery.CountAsync(ct);
        // Project on the server-side within the query
        var items = await query.Select(selector).ToListAsync(ct);
        return new PagedList<TResult>
        {
            Items = items.AsReadOnly(),
            TotalCount = total,
            Page = page,
            PageSize = size,
        };
    }

    public async Task<bool> AnyAsync(
        ISpecification<T> spec, CancellationToken ct)
        => await ApplySpec(Set.AsQueryable(), spec).AnyAsync(ct);
}
