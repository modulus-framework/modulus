namespace Modulus.Data.Abstractions;

using System.Linq.Expressions;
using Modulus.Core.Abstractions.Common;

/// <summary>
/// Write-side generic repository contract, implemented per persistence technology.
/// </summary>
public interface IRepository<T> where T : class
{
    Task<T?> GetByIdAsync(object id, CancellationToken ct);
    Task<IReadOnlyList<T>> ListAsync(ISpecification<T> spec, CancellationToken ct);

    /// <summary>Returns the first entity matching the spec, or null if none match.</summary>
    Task<T?> FirstOrDefaultAsync(ISpecification<T> spec, CancellationToken ct);

    /// <summary>Returns the only entity matching the spec; throws if zero or more than one match.</summary>
    Task<T> SingleAsync(ISpecification<T> spec, CancellationToken ct);

    /// <summary>Returns the only entity matching the spec, or null if zero match; throws if more than one.</summary>
    Task<T?> SingleOrDefaultAsync(ISpecification<T> spec, CancellationToken ct);

    /// <summary>Streams entities matching the spec without buffering all results.</summary>
    IAsyncEnumerable<T> AsAsyncEnumerable(ISpecification<T> spec);

    Task<int> CountAsync(ISpecification<T> spec, CancellationToken ct);
    Task AddAsync(T entity, CancellationToken ct);
    Task AddRangeAsync(IEnumerable<T> entities, CancellationToken ct);
    Task UpdateAsync(T entity, CancellationToken ct);
    Task DeleteAsync(T entity, CancellationToken ct);

    /// <summary>Deletes multiple entities matching the spec in a single operation.</summary>
    Task DeleteRangeAsync(ISpecification<T> spec, CancellationToken ct);
}

/// <summary>Read-side repository contract (query only, no side effects).</summary>
public interface IReadRepository<T> where T : class
{
    Task<T?> GetByIdAsync(object id, CancellationToken ct);
    Task<IReadOnlyList<T>> ListAsync(ISpecification<T> spec, CancellationToken ct);

    /// <summary>
    /// Fetches a paged result set with server-side projection.
    /// The expression is executed on the server (e.g., in SQL), materializing
    /// only the projected columns—not full entities.
    /// </summary>
    Task<PagedList<TResult>> ListPagedAsync<TResult>(
        ISpecification<T> spec,
        Expression<Func<T, TResult>> selector,
        int page, int size,
        CancellationToken ct);

    /// <summary>
    /// Keyset (seek) paging: the next <paramref name="size"/> items after <paramref name="after"/> in
    /// <paramref name="keySelector"/> order, without a total count. Pass <see langword="null"/> for the first page.
    /// The key must be unique (for example an id that is an integer or a timestamp, not a Guid) and orderable by the
    /// provider. The specification must not carry Skip or Take, because the key is the position.
    /// </summary>
    Task<KeysetPage<TResult>> ListKeysetAsync<TKey, TResult>(
        ISpecification<T> spec,
        Expression<Func<T, TKey>> keySelector,
        Expression<Func<T, TResult>> selector,
        TKey? after,
        int size,
        CancellationToken ct)
        where TKey : struct, IComparable<TKey>
        => Task.FromException<KeysetPage<TResult>>(
            new NotSupportedException($"{GetType().Name} does not support keyset paging."));

    Task<int> CountAsync(ISpecification<T> spec, CancellationToken ct);
    Task<bool> AnyAsync(ISpecification<T> spec, CancellationToken ct);
}
