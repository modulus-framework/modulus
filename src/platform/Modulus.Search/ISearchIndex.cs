namespace Modulus.Search;

using System.Text.Json;

/// <summary>
/// Full-text search over JSON documents, the same for OpenSearch and Solr. An index is an OpenSearch index or a Solr core; its name is
/// lowercase letters, digits, '-' and '_' (for example <c>products</c>). Every document has a string id.
/// </summary>
public interface ISearchIndex
{
    /// <summary>
    /// Creates the OpenSearch index when it does not exist. For Solr it checks that the core exists and fails with the command that creates
    /// it when it does not (cores are created on the server). Safe to call on every start.
    /// </summary>
    Task EnsureIndexAsync(string index, CancellationToken ct = default);

    /// <summary>Adds the document with <paramref name="id"/>, or replaces the one already stored under it.</summary>
    Task IndexAsync(string index, string id, IReadOnlyDictionary<string, object?> document, CancellationToken ct = default);

    /// <summary>Removes the document. A document that is not there is not an error.</summary>
    Task DeleteAsync(string index, string id, CancellationToken ct = default);

    /// <summary>Searches the index. Text is matched against all text fields; filters are exact matches on keyword fields.</summary>
    Task<SearchResult> SearchAsync(string index, SearchQuery query, CancellationToken ct = default);
}

/// <summary>A search: the text (empty matches everything), the page (From, Size) and exact-match filters (field to value).</summary>
public sealed record SearchQuery
{
    /// <summary>The words to find. Operators typed by a user are not interpreted: the text is matched as plain words.</summary>
    public string Text { get; init; } = "";

    /// <summary>The number of hits to skip. Zero or more.</summary>
    public int From { get; init; }

    /// <summary>The number of hits to return, from 1 to 100.</summary>
    public int Size { get; init; } = 20;

    /// <summary>Exact-match conditions, one field to one value each. All must hold.</summary>
    public IReadOnlyDictionary<string, string> Filters { get; init; } = new Dictionary<string, string>();
}

/// <summary>One page of results: the total number of matches, and the hits on this page.</summary>
public sealed record SearchResult(long Total, IReadOnlyList<SearchHit> Hits);

/// <summary>A matching document: its id and the stored document as JSON.</summary>
public sealed record SearchHit(string Id, JsonElement Document);
