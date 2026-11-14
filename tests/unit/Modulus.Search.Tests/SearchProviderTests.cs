using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Modulus.Search;
using Xunit;

namespace Modulus.Search.Tests;

/// <summary>The OpenSearch and Solr providers over a scripted HTTP handler: the requests they send, the results they read, and the refusals.</summary>
[Trait("Category", "Unit")]
public sealed class SearchProviderTests
{
    /// <summary>Answers each request with the next scripted response and records what was sent.</summary>
    private sealed class ScriptedHandler(params (HttpStatusCode Status, string Body)[] script) : HttpMessageHandler
    {
        private int _next;
        public List<(HttpMethod Method, string Url, string? Body)> Sent { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            Sent.Add((request.Method, request.RequestUri!.PathAndQuery, body));
            var (status, text) = _next < script.Length ? script[_next++] : (HttpStatusCode.OK, "{}");
            return new HttpResponseMessage(status) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
        }
    }

    private static HttpClient Client(HttpMessageHandler handler) => new(handler) { BaseAddress = new Uri("https://search.example.com/") };

    private static IReadOnlyDictionary<string, object?> Doc(params (string Key, object? Value)[] fields)
        => fields.ToDictionary(f => f.Key, f => f.Value, StringComparer.Ordinal);

    [Fact]
    public async Task OpenSearch_creates_a_missing_index_and_leaves_an_existing_one_alone()
    {
        var missing = new ScriptedHandler((HttpStatusCode.NotFound, ""), (HttpStatusCode.OK, "{}"));
        await new OpenSearchIndex(Client(missing), Options()).EnsureIndexAsync("products");
        missing.Sent.Select(s => (s.Method.Method, s.Url)).Should().Equal(("HEAD", "/products"), ("PUT", "/products"));

        var present = new ScriptedHandler((HttpStatusCode.OK, ""));
        await new OpenSearchIndex(Client(present), Options()).EnsureIndexAsync("products");
        present.Sent.Should().HaveCount(1);
    }

    [Fact]
    public async Task OpenSearch_indexes_a_document_under_its_id_and_waits_for_refresh_when_asked()
    {
        var handler = new ScriptedHandler((HttpStatusCode.Created, "{}"));
        var index = new OpenSearchIndex(Client(handler), Options(refresh: true));

        await index.IndexAsync("products", "p-1", Doc(("name", "Red kettle"), ("price", 42)));

        handler.Sent.Single().Method.Should().Be(HttpMethod.Put);
        handler.Sent.Single().Url.Should().Be("/products/_doc/p-1?refresh=wait_for");
        handler.Sent.Single().Body.Should().Contain("\"name\":\"Red kettle\"").And.Contain("\"price\":42");
    }

    [Fact]
    public async Task OpenSearch_delete_treats_a_missing_document_as_done()
    {
        var handler = new ScriptedHandler((HttpStatusCode.NotFound, "{}"));

        await new OpenSearchIndex(Client(handler), Options()).DeleteAsync("products", "p-9");

        handler.Sent.Single().Url.Should().Be("/products/_doc/p-9");
    }

    [Fact]
    public async Task OpenSearch_search_sends_plain_text_and_exact_filters_and_reads_the_total_and_hits()
    {
        const string response = """
            {"hits":{"total":{"value":2,"relation":"eq"},"hits":[
              {"_id":"p-1","_source":{"name":"Red kettle"}},
              {"_id":"p-7","_source":{"name":"Red mug"}}]}}
            """;
        var handler = new ScriptedHandler((HttpStatusCode.OK, response));
        var query = new SearchQuery { Text = "red", From = 20, Size = 10, Filters = new Dictionary<string, string> { ["category"] = "kitchen" } };

        var result = await new OpenSearchIndex(Client(handler), Options()).SearchAsync("products", query);

        result.Total.Should().Be(2);
        result.Hits.Select(h => h.Id).Should().Equal("p-1", "p-7");
        result.Hits[0].Document.GetProperty("name").GetString().Should().Be("Red kettle");

        var sent = handler.Sent.Single();
        sent.Url.Should().Be("/products/_search");
        sent.Body.Should().Contain("\"simple_query_string\"").And.Contain("\"query\":\"red\"")
            .And.Contain("\"from\":20").And.Contain("\"size\":10")
            .And.Contain("\"term\":{\"category\":\"kitchen\"}");
    }

    [Fact]
    public async Task OpenSearch_empty_text_matches_everything()
    {
        var handler = new ScriptedHandler((HttpStatusCode.OK, """{"hits":{"total":0,"hits":[]}}"""));

        var result = await new OpenSearchIndex(Client(handler), Options()).SearchAsync("products", new SearchQuery());

        result.Total.Should().Be(0);
        handler.Sent.Single().Body.Should().Contain("\"match_all\"");
    }

    [Fact]
    public async Task A_refusal_names_the_status_but_not_the_document_it_was_sending()
    {
        var handler = new ScriptedHandler((HttpStatusCode.BadRequest, """{"error":"mapping conflict on iban DE89370400440532013000"}"""));

        var act = () => new OpenSearchIndex(Client(handler), Options()).IndexAsync("products", "p-1", Doc(("iban", "DE89370400440532013000")));

        var error = await act.Should().ThrowAsync<InvalidOperationException>();
        error.Which.Message.Should().Contain("400").And.NotContain("DE89370400440532013000");
    }

    [Theory]
    [InlineData("Products")]
    [InlineData("shop/products")]
    [InlineData("")]
    public async Task An_index_name_that_is_not_a_path_safe_word_is_refused_before_any_request(string index)
    {
        var handler = new ScriptedHandler();

        var act = () => new OpenSearchIndex(Client(handler), Options()).SearchAsync(index, new SearchQuery());

        await act.Should().ThrowAsync<ArgumentException>();
        handler.Sent.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task A_page_size_outside_one_to_a_hundred_is_refused(int size)
    {
        var handler = new ScriptedHandler();

        var act = () => new OpenSearchIndex(Client(handler), Options()).SearchAsync("products", new SearchQuery { Size = size });

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        handler.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Solr_refuses_a_missing_core_and_says_how_to_create_it()
    {
        // Solr lists a core that does not exist as an empty entry.
        var handler = new ScriptedHandler((HttpStatusCode.OK, """{"status":{"products":{}}}"""));

        var act = () => new SolrIndex(Client(handler), Options()).EnsureIndexAsync("products");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*solr precreate products*");
        handler.Sent.Should().HaveCount(1, "a missing core is reported, never created by Modulus");
        handler.Sent.Single().Url.Should().Be("/solr/admin/cores?action=STATUS&core=products&wt=json");
    }

    [Fact]
    public async Task Solr_leaves_an_existing_core_alone()
    {
        var handler = new ScriptedHandler((HttpStatusCode.OK, """{"status":{"products":{"name":"products"}}}"""));

        await new SolrIndex(Client(handler), Options()).EnsureIndexAsync("products");

        handler.Sent.Should().HaveCount(1);
    }

    [Fact]
    public async Task Solr_indexes_a_document_with_its_id_and_commits_on_request()
    {
        var handler = new ScriptedHandler((HttpStatusCode.OK, "{}"));

        await new SolrIndex(Client(handler), Options(refresh: true)).IndexAsync("products", "p-1", Doc(("name", "Red kettle")));

        var sent = handler.Sent.Single();
        sent.Url.Should().Be("/solr/products/update/json/docs?commit=true");
        sent.Body.Should().Contain("\"id\":\"p-1\"").And.Contain("\"name\":\"Red kettle\"")
            .And.Contain("\"_text_\":\"Red kettle\"");
    }

    [Fact]
    public async Task Solr_deletes_by_id()
    {
        var handler = new ScriptedHandler((HttpStatusCode.OK, "{}"));

        await new SolrIndex(Client(handler), Options()).DeleteAsync("products", "p-1");

        handler.Sent.Single().Url.Should().Be("/solr/products/update?commitWithin=1000");
        handler.Sent.Single().Body.Should().Be("{\"delete\":{\"id\":\"p-1\"}}");
    }

    [Fact]
    public async Task Solr_search_escapes_the_text_and_quotes_the_filters()
    {
        const string response = """{"response":{"numFound":1,"start":0,"docs":[{"id":"p-1","name":"Red kettle"}]}}""";
        var handler = new ScriptedHandler((HttpStatusCode.OK, response));
        var query = new SearchQuery
        {
            Text = "red kettle:OR (x)",
            From = 5,
            Size = 3,
            Filters = new Dictionary<string, string> { ["category"] = "kitchen \"deluxe\"" },
        };

        var result = await new SolrIndex(Client(handler), Options()).SearchAsync("products", query);

        result.Total.Should().Be(1);
        result.Hits.Single().Id.Should().Be("p-1");
        var url = Uri.UnescapeDataString(handler.Sent.Single().Url);
        url.Should().Contain("q=red kettle\\:OR \\(x\\)")
            .And.Contain("fq=category:\"kitchen \\\"deluxe\\\"\"")
            .And.Contain("start=5").And.Contain("rows=3");
    }

    [Fact]
    public async Task Solr_hits_read_single_valued_fields_as_plain_values()
    {
        const string response = """{"response":{"numFound":1,"start":0,"docs":[{"id":"p-1","name":["Red kettle"],"tags":["a","b"]}]}}""";
        var handler = new ScriptedHandler((HttpStatusCode.OK, response));

        var result = await new SolrIndex(Client(handler), Options()).SearchAsync("products", new SearchQuery { Text = "red" });

        var document = result.Hits.Single().Document;
        document.GetProperty("name").GetString().Should().Be("Red kettle");
        document.GetProperty("tags").GetArrayLength().Should().Be(2);
        handler.Sent.Single().Url.Should().Contain("qf=_text_");
    }

    [Fact]
    public void Text_escaping_turns_every_query_operator_into_a_plain_character()
    {
        SolrIndex.EscapeText("a+b -c && d || !e (f) {g} [h] ^i \"j\" ~k *l ?m :n \\o /p").Should()
            .Be("a\\+b \\-c \\&\\& d \\|\\| \\!e \\(f\\) \\{g\\} \\[h\\] \\^i \\\"j\\\" \\~k \\*l \\?m \\:n \\\\o \\/p");
    }

    [Fact]
    public void Registration_refuses_a_server_address_that_is_not_http()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Search:BaseUrl"] = "ftp://search" }).Build();
        var services = new ServiceCollection();
        services.AddModulusOpenSearch(configuration);
        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IOptions<SearchOptions>>().Value;

        act.Should().Throw<Microsoft.Extensions.Options.OptionsValidationException>().WithMessage("*Search:BaseUrl*");
    }

    [Fact]
    public void Registration_refuses_a_user_name_without_a_password()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Search:BaseUrl"] = "https://search.example.com",
            ["Search:Username"] = "app",
        }).Build();
        var services = new ServiceCollection();
        services.AddModulusSolr(configuration);
        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IOptions<SearchOptions>>().Value;

        act.Should().Throw<Microsoft.Extensions.Options.OptionsValidationException>().WithMessage("*Search:Password*");
    }

    private static IOptions<SearchOptions> Options(bool refresh = false)
        => Microsoft.Extensions.Options.Options.Create(new SearchOptions { BaseUrl = "https://search.example.com/", RefreshOnWrite = refresh });
}
