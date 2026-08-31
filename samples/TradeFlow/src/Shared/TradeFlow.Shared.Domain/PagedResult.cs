namespace TradeFlow.Shared.Domain;

/// <summary>
/// Represents a paged result of a query.
/// </summary>
/// <typeparam name="T">The type of items in the result.</typeparam>
public sealed record PagedResult<T>
{
    public List<T> Items { get; init; }

    public int TotalCount { get; init; }

    public int PageNumber { get; init; }

    public int PageSize { get; init; }

    public int TotalPages => PageSize > 0
        ? (int)Math.Ceiling((double)TotalCount / PageSize)
        : 0;

    public bool HasPreviousPage => PageNumber > 1;

    public bool HasNextPage => PageNumber < TotalPages;

    public PagedResult(List<T> items, int totalCount, int pageNumber, int pageSize, int totalPages = 0)
    {
        Items = items;
        TotalCount = totalCount;
        PageNumber = pageNumber;
        PageSize = pageSize;
    }
}

/// <summary>
/// Represents pagination parameters.
/// </summary>
public sealed class PaginationParameters
{
    /// <summary>
    /// Gets the default page size.
    /// </summary>
    public const int DefaultPageSize = 20;

    /// <summary>
    /// Gets the maximum page size.
    /// </summary>
    public const int MaxPageSize = 100;

    private int _page = 1;
    private int _pageSize = DefaultPageSize;

    /// <summary>
    /// Gets or sets the page number (1-based).
    /// </summary>
    public int Page
    {
        get => _page;
        set => _page = value < 1 ? 1 : value;
    }

    /// <summary>
    /// Gets or sets the page size.
    /// </summary>
    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = value < 1 ? DefaultPageSize : (value > MaxPageSize ? MaxPageSize : value);
    }

    /// <summary>
    /// Gets the number of items to skip.
    /// </summary>
    public int Skip => (Page - 1) * PageSize;

    /// <summary>
    /// Gets the number of items to take.
    /// </summary>
    public int Take => PageSize;
}
