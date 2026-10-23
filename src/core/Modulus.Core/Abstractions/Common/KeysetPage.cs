namespace Modulus.Core.Abstractions.Common;

/// <summary>How a list request pages through its results. The caller picks one per request.</summary>
public enum PagingMode
{
    /// <summary>Page number and page size, with the total count. Deep pages get slower, and rows shift when others are inserted.</summary>
    Offset,

    /// <summary>The caller sends the key of the last item it has seen. No total count, and no skipped or repeated rows.</summary>
    Keyset,

    /// <summary>Keyset paging with an opaque token that the server issued in the previous page's <see cref="KeysetPage{T}.NextCursor"/>.</summary>
    Cursor,
}

/// <summary>One page of a keyset or cursor listing.</summary>
public sealed record KeysetPage<T>
{
    public IReadOnlyList<T> Items { get; init; } = [];

    /// <summary>True when at least one more item follows this page. Keyset listings do not count the total.</summary>
    public bool HasNextPage { get; init; }

    /// <summary>The position of the last item on this page, encoded by <c>CursorCodec</c>; null when there is no next page.</summary>
    public string? NextCursor { get; init; }

    public KeysetPage<TResult> Map<TResult>(Func<T, TResult> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        return new KeysetPage<TResult>
        {
            Items = Items.Select(selector).ToList().AsReadOnly(),
            HasNextPage = HasNextPage,
            NextCursor = NextCursor,
        };
    }
}
