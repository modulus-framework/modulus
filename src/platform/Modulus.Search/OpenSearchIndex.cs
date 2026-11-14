namespace Modulus.Search;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

/// <summary>
/// The OpenSearch provider, over its REST API. Text is matched with <c>simple_query_string</c>, which reads a user's input as plain
/// words (no field names, no wildcards from the user). Filters are <c>term</c> queries on keyword fields. Errors name the status code
/// only: a response body can carry document content.
/// </summary>
public sealed class OpenSearchIndex(HttpClient http, IOptions<SearchOptions> options) : ISearchIndex
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task EnsureIndexAsync(string index, CancellationToken ct = default)
    {
        var name = SearchNames.RequireIndex(index);
        using var exists = await SendAsync(HttpMethod.Head, Escape(name), null, ct);
        if (exists.StatusCode == HttpStatusCode.OK)
            return;
        if (exists.StatusCode != HttpStatusCode.NotFound)
            throw Refused("check the index", exists.StatusCode);

        using var created = await SendAsync(HttpMethod.Put, Escape(name), new { }, ct);
        // Another replica may have created it between the two calls; that is the state we wanted.
        if (!created.IsSuccessStatusCode && created.StatusCode != HttpStatusCode.BadRequest)
            throw Refused("create the index", created.StatusCode);
    }

    public async Task IndexAsync(string index, string id, IReadOnlyDictionary<string, object?> document, CancellationToken ct = default)
    {
        var name = SearchNames.RequireIndex(index);
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentNullException.ThrowIfNull(document);
        using var response = await SendAsync(HttpMethod.Put, $"{Escape(name)}/_doc/{Uri.EscapeDataString(id)}{Refresh()}", document, ct);
        if (!response.IsSuccessStatusCode)
            throw Refused("index the document", response.StatusCode);
    }

    public async Task DeleteAsync(string index, string id, CancellationToken ct = default)
    {
        var name = SearchNames.RequireIndex(index);
        ArgumentException.ThrowIfNullOrEmpty(id);
        using var response = await SendAsync(HttpMethod.Delete, $"{Escape(name)}/_doc/{Uri.EscapeDataString(id)}{Refresh()}", null, ct);
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
            throw Refused("delete the document", response.StatusCode);
    }

    public async Task<SearchResult> SearchAsync(string index, SearchQuery query, CancellationToken ct = default)
    {
        var name = SearchNames.RequireIndex(index);
        query = SearchNames.RequireQuery(query);

        object must = string.IsNullOrWhiteSpace(query.Text)
            ? new { match_all = new { } }
            : new { simple_query_string = new { query = query.Text, fields = new[] { "*" }, default_operator = "and" } };
        var filters = query.Filters.Select(f => (object)new { term = new Dictionary<string, string> { [f.Key] = f.Value } }).ToArray();
        var body = new
        {
            from = query.From,
            size = query.Size,
            query = new { @bool = new { must = new[] { must }, filter = filters } },
        };

        using var response = await SendAsync(HttpMethod.Post, $"{Escape(name)}/_search", body, ct);
        if (!response.IsSuccessStatusCode)
            throw Refused("search", response.StatusCode);

        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var hits = document.RootElement.GetProperty("hits");
        var total = hits.GetProperty("total");
        var count = total.ValueKind == JsonValueKind.Object ? total.GetProperty("value").GetInt64() : total.GetInt64();
        var page = new List<SearchHit>();
        foreach (var hit in hits.GetProperty("hits").EnumerateArray())
            page.Add(new SearchHit(hit.GetProperty("_id").GetString()!, hit.GetProperty("_source").Clone()));
        return new SearchResult(count, page);
    }

    private string Refresh() => options.Value.RefreshOnWrite ? "?refresh=wait_for" : "";

    private static string Escape(string name) => Uri.EscapeDataString(name);

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        var request = new HttpRequestMessage(method, path);
        if (body is not null)
            request.Content = JsonContent.Create(body, options: Json);
        return http.SendAsync(request, ct);
    }

    private static InvalidOperationException Refused(string action, HttpStatusCode status)
        => new($"OpenSearch refused to {action} (status {(int)status}).");
}
