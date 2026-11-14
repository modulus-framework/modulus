namespace Modulus.Search;

using System.Text.RegularExpressions;

/// <summary>Input checks shared by the providers.</summary>
internal static partial class SearchNames
{
    /// <summary>The index name, checked: it becomes part of a URL path and must be valid for both servers.</summary>
    public static string RequireIndex(string index)
        => IndexName().IsMatch(index)
            ? index
            : throw new ArgumentException("An index name is lowercase letters, digits, '-' and '_', for example products.", nameof(index));

    /// <summary>The query, checked: a page of 1 to 100 hits, no negative offset.</summary>
    public static SearchQuery RequireQuery(SearchQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.From < 0)
            throw new ArgumentOutOfRangeException(nameof(query), "From must be zero or more.");
        if (query.Size is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(query), "Size must be from 1 to 100.");
        return query;
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9_-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex IndexName();
}
