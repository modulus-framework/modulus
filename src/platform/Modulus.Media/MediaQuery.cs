namespace Modulus.Media;

/// <summary>A page of the media list. <see cref="Search"/> matches the display name.</summary>
public sealed record MediaQuery(string? Search = null, MediaKind? Kind = null, int Page = 1, int PageSize = 50)
{
    /// <summary>The largest page size a caller may ask for.</summary>
    public const int MaxPageSize = 200;
}

/// <summary>One page of media, with the total number that match the query.</summary>
public sealed record MediaPage(IReadOnlyList<MediaItem> Items, int Total);
