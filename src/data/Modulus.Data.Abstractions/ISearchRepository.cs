namespace Modulus.Data.Abstractions;

/// <summary>Full-text / faceted search contract (Elasticsearch, etc.).</summary>
public interface ISearchRepository<T> where T : class
{
    Task<SearchResult<T>> SearchAsync(SearchRequest request, CancellationToken ct);
    Task IndexAsync(T document, CancellationToken ct);
    Task IndexBulkAsync(IEnumerable<T> documents, CancellationToken ct);
    Task DeleteFromIndexAsync(object id, CancellationToken ct);
}

/// <summary>Distributed cache contract (Redis, etc.).</summary>
public interface ICacheRepository
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct);
    Task SetAsync<T>(string key, T value, TimeSpan? ttl, CancellationToken ct);
    Task RemoveAsync(string key, CancellationToken ct);
    Task RemoveByPatternAsync(string pattern, CancellationToken ct);
    Task<bool> ExistsAsync(string key, CancellationToken ct);
}

public sealed record SearchRequest(
    string Term,
    int    Page     = 1,
    int    PageSize = 20,
    Dictionary<string, string>? Filters = null,
    string? SortBy   = null,
    bool    SortDesc = false);

public sealed record SearchResult<T>(
    IReadOnlyList<T> Items,
    long             TotalCount,
    int              Page,
    int              PageSize,
    long             TookMs = 0)
{
    public SearchResult<TResult> Map<TResult>(Func<T, TResult> selector)
        => new(Items.Select(selector).ToList(), TotalCount, Page, PageSize, TookMs);
}
