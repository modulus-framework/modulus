namespace Modulus.Core.Abstractions.Common;

/// <summary>Paged result set returned by list queries.</summary>
public sealed record PagedList<T>
{
    public IReadOnlyList<T> Items      { get; init; } = [];
    public int              TotalCount { get; init; }
    public int              Page       { get; init; }
    public int              PageSize   { get; init; }

    public bool HasNextPage     => Page * PageSize < TotalCount;
    public bool HasPreviousPage => Page > 1;
    public int  TotalPages      => (int)Math.Ceiling(TotalCount / (double)PageSize);

    public PagedList<TResult> Map<TResult>(Func<T, TResult> selector)
        => new()
        {
            Items      = Items.Select(selector).ToList().AsReadOnly(),
            TotalCount = TotalCount,
            Page       = Page,
            PageSize   = PageSize,
        };
}