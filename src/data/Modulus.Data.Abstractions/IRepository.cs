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

    Task<int> CountAsync(ISpecification<T> spec, CancellationToken ct);
    Task<bool> AnyAsync(ISpecification<T> spec, CancellationToken ct);
}
