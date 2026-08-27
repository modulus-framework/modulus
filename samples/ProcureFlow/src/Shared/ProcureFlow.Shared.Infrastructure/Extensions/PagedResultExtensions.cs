using ProcureFlow.Shared.Domain;
using Microsoft.EntityFrameworkCore;

namespace ProcureFlow.Shared.Infrastructure.Extensions;

/// <summary>
/// Extension methods for creating paged results from IQueryable.
/// These are placed in Infrastructure as they depend on Entity Framework Core.
/// </summary>
public static class PagedResultExtensions
{
    /// <summary>
    /// Converts an IQueryable to a PagedResult with efficient database-level pagination.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="queryable">The queryable to paginate.</param>
    /// <param name="pageNumber">The page number (1-based).</param>
    /// <param name="pageSize">The page size.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A paged result with the specified page of data.</returns>
    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(
        this IQueryable<T> queryable,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (pageNumber < 1)
        {
            pageNumber = 1;
        }

        if (pageSize < 1)
        {
            pageSize = 10;
        }

        int totalCount = await queryable.CountAsync(cancellationToken);
        List<T> items = await queryable
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<T>(
            items,
            totalCount,
            pageNumber,
            pageSize);
    }
}
