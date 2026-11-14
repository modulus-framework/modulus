namespace Modulus.Search;

/// <summary>
/// The search server (<c>Search</c>): the base address of OpenSearch or Solr, optional basic-authentication credentials, and whether a
/// write is visible to the next search at once. The password is a secret: user secrets or the environment only.
/// </summary>
public sealed class SearchOptions
{
    /// <summary>The configuration section these options bind from.</summary>
    public const string SectionName = "Search";

    /// <summary>The server's base address, for example <c>https://search.example.com:9200/</c> (OpenSearch) or <c>http://solr:8983/</c> (Solr).</summary>
    public string? BaseUrl { get; set; }

    /// <summary>The user name, when the server requires basic authentication.</summary>
    public string? Username { get; set; }

    /// <summary>The password for <see cref="Username"/>. A secret.</summary>
    public string? Password { get; set; }

    /// <summary>
    /// When true, every write is visible to the next search at once (OpenSearch <c>refresh=wait_for</c>, Solr <c>commit=true</c>).
    /// Off by default: the server makes writes searchable within about a second, which is cheaper under load.
    /// </summary>
    public bool RefreshOnWrite { get; set; }
}
