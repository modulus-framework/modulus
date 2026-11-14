namespace Modulus.Search;

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

/// <summary>
/// The Apache Solr provider, over its JSON APIs: the update handler for writes, <c>select</c> for searches, the core admin API to
/// check that a core exists. Text is escaped before it reaches the query parser, so a user's input
/// cannot add field names or operators. Filters are exact matches (<c>field:"value"</c>). Documents are stored under <c>id</c>.
/// Cores are created on the server (<c>solr precreate</c>); <see cref="EnsureIndexAsync"/> checks that the core exists and says how to create it otherwise.
/// Requires standalone cores; SolrCloud collections are not supported by this provider.
/// </summary>
public sealed class SolrIndex(HttpClient http, IOptions<SearchOptions> options) : ISearchIndex
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task EnsureIndexAsync(string index, CancellationToken ct = default)
    {
        var core = SearchNames.RequireIndex(index);
        using var status = await http.GetAsync($"solr/admin/cores?action=STATUS&core={Uri.EscapeDataString(core)}&wt=json", ct);
        if (!status.IsSuccessStatusCode)
            throw Refused("check the core", status.StatusCode);

        using var json = await JsonDocument.ParseAsync(await status.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        // Solr lists a core that does not exist as an empty entry; a live core has a name.
        if (json.RootElement.GetProperty("status").TryGetProperty(core, out var entry) && entry.TryGetProperty("name", out _))
            return;

        throw new InvalidOperationException(
            $"Solr has no core named {core}. Create it on the server before the application starts, for example with " +
            $"'solr precreate {core}' (in Docker: docker exec <container> solr precreate {core}). Modulus does not create cores: " +
            "the default configset is not reachable through the core admin API.");
    }

    public async Task IndexAsync(string index, string id, IReadOnlyDictionary<string, object?> document, CancellationToken ct = default)
    {
        var core = SearchNames.RequireIndex(index);
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentNullException.ThrowIfNull(document);
        var body = new Dictionary<string, object?>(document, StringComparer.Ordinal) { ["id"] = id, [TextField] = AllText(document) };
        using var response = await http.PostAsync($"solr/{Uri.EscapeDataString(core)}/update/json/docs{Commit()}", JsonContent.Create(body, options: Json), ct);
        if (!response.IsSuccessStatusCode)
            throw Refused("index the document", response.StatusCode);
    }

    public async Task DeleteAsync(string index, string id, CancellationToken ct = default)
    {
        var core = SearchNames.RequireIndex(index);
        ArgumentException.ThrowIfNullOrEmpty(id);
        using var response = await http.PostAsync($"solr/{Uri.EscapeDataString(core)}/update{Commit()}", JsonContent.Create(new { delete = new { id } }, options: Json), ct);
        if (!response.IsSuccessStatusCode)
            throw Refused("delete the document", response.StatusCode);
    }

    public async Task<SearchResult> SearchAsync(string index, SearchQuery query, CancellationToken ct = default)
    {
        var core = SearchNames.RequireIndex(index);
        query = SearchNames.RequireQuery(query);

        var text = string.IsNullOrWhiteSpace(query.Text) ? "*:*" : EscapeText(query.Text);
        var url = new StringBuilder($"solr/{Uri.EscapeDataString(core)}/select?defType=edismax&qf={TextField}&q.op=AND&wt=json")
            .Append("&q=").Append(Uri.EscapeDataString(text))
            .Append("&start=").Append(query.From)
            .Append("&rows=").Append(query.Size);
        foreach (var filter in query.Filters)
            url.Append("&fq=").Append(Uri.EscapeDataString($"{filter.Key}:\"{EscapeQuoted(filter.Value)}\""));

        using var response = await http.GetAsync(url.ToString(), ct);
        if (!response.IsSuccessStatusCode)
            throw Refused("search", response.StatusCode);

        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var result = document.RootElement.GetProperty("response");
        var page = new List<SearchHit>();
        foreach (var doc in result.GetProperty("docs").EnumerateArray())
            page.Add(new SearchHit(doc.GetProperty("id").GetString()!, Unwrap(doc)));
        return new SearchResult(result.GetProperty("numFound").GetInt64(), page);
    }

    /// <summary>
    /// The field that every search reads. The _default configset defines it as a multi-valued text field, but nothing fills it from the
    /// other fields, so Modulus writes the document's string values into it: a search then matches any text field.
    /// </summary>
    internal const string TextField = "_text_";

    internal static string AllText(IReadOnlyDictionary<string, object?> document)
        => string.Join(" ", document.Values.OfType<string>().Where(v => v.Length > 0));

    /// <summary>
    /// Solr returns every field of a schemaless document as an array, even a single value. A one-element array is unwrapped so that a hit
    /// reads the same as from OpenSearch (<c>"name": "Red kettle"</c>); longer arrays are returned as they are.
    /// </summary>
    internal static JsonElement Unwrap(JsonElement document)
    {
        var node = JsonNode.Parse(document.GetRawText())!.AsObject();
        foreach (var key in node.Select(p => p.Key).ToList())
        {
            if (node[key] is JsonArray { Count: 1 } array)
                node[key] = array[0]?.DeepClone();
        }
        return JsonSerializer.SerializeToElement(node);
    }

    private string Commit() => options.Value.RefreshOnWrite ? "?commit=true" : "?commitWithin=1000";

    /// <summary>Escapes every character the Lucene query parser reads as syntax, so the text is only ever words.</summary>
    internal static string EscapeText(string text)
    {
        var builder = new StringBuilder(text.Length + 8);
        foreach (var c in text)
        {
            if ("+-&|!(){}[]^\"~*?:\\/".Contains(c, StringComparison.Ordinal))
                builder.Append('\\');
            builder.Append(c);
        }
        return builder.ToString();
    }

    /// <summary>Escapes a value placed inside a quoted phrase (a backslash and a double quote).</summary>
    internal static string EscapeQuoted(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    private static InvalidOperationException Refused(string action, HttpStatusCode status)
        => new($"Solr refused to {action} (status {(int)status}).");
}
