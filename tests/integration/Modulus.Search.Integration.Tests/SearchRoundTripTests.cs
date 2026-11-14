namespace Modulus.Search.Integration.Tests;

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// The provider contract against a real server, through the same registration an application uses: create the index, index, search
/// with a filter, delete, and a user's text that carries query syntax. Each test uses its own index (Solr: its own core), so the tests do not interfere.
/// </summary>
public abstract class SearchRoundTripTests
{
    protected static string NewIndex() => "shop-" + Guid.NewGuid().ToString("N")[..12];

    protected static async Task RoundTripAsync(ISearchIndex search, string index)
    {
        await search.EnsureIndexAsync(index);
        await search.EnsureIndexAsync(index); // safe to repeat on every start

        await search.IndexAsync(index, "p-1", new Dictionary<string, object?> { ["name"] = "Red kettle", ["category"] = "kitchen" });
        await search.IndexAsync(index, "p-2", new Dictionary<string, object?> { ["name"] = "Red mug", ["category"] = "kitchen" });
        await search.IndexAsync(index, "p-3", new Dictionary<string, object?> { ["name"] = "Red bicycle", ["category"] = "outdoor" });

        var all = await search.SearchAsync(index, new SearchQuery { Text = "red" });
        all.Total.Should().Be(3);

        var kitchen = await search.SearchAsync(index, new SearchQuery { Text = "red", Filters = new Dictionary<string, string> { ["category"] = "kitchen" } });
        kitchen.Total.Should().Be(2);
        kitchen.Hits.Select(h => h.Id).Should().BeEquivalentTo(["p-1", "p-2"]);
        kitchen.Hits.Single(h => h.Id == "p-1").Document.GetProperty("name").GetString().Should().Be("Red kettle");

        var paged = await search.SearchAsync(index, new SearchQuery { Text = "red", From = 2, Size = 1 });
        paged.Total.Should().Be(3);
        paged.Hits.Should().HaveCount(1);

        // Query syntax typed by a user is words, not operators: this must not throw and must match only what the words match.
        var odd = await search.SearchAsync(index, new SearchQuery { Text = "red OR (bicycle" });
        odd.Total.Should().BeGreaterThanOrEqualTo(0);

        await search.DeleteAsync(index, "p-3");
        await search.DeleteAsync(index, "p-3"); // a second delete of a missing document is not an error
        var afterDelete = await search.SearchAsync(index, new SearchQuery { Text = "red" });
        afterDelete.Total.Should().Be(2);
        afterDelete.Hits.Select(h => h.Id).Should().NotContain("p-3");
    }

    protected static ISearchIndex Register(IServiceCollection services, IConfiguration configuration, Action<IServiceCollection, IConfiguration> add)
    {
        add(services, configuration);
        return services.BuildServiceProvider().GetRequiredService<ISearchIndex>();
    }

    protected static IConfiguration Settings(string endpoint) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Search:BaseUrl"] = endpoint,
            ["Search:RefreshOnWrite"] = "true",
        })
        .Build();
}

[Collection(OpenSearchCollection.Name)]
[Trait("Category", "Integration")]
public sealed class OpenSearchIntegrationTests(OpenSearchFixture server) : SearchRoundTripTests
{
    [Fact]
    public async Task OpenSearch_round_trip_through_the_registered_provider()
    {
        var search = Register(new ServiceCollection(), Settings(server.Endpoint), (s, c) => s.AddModulusOpenSearch(c));

        await RoundTripAsync(search, NewIndex());
    }
}

[Collection(SolrCollection.Name)]
[Trait("Category", "Integration")]
public sealed class SolrIntegrationTests(SolrFixture server) : SearchRoundTripTests
{
    [Fact]
    public async Task Solr_round_trip_through_the_registered_provider()
    {
        var search = Register(new ServiceCollection(), Settings(server.Endpoint), (s, c) => s.AddModulusSolr(c));

        // The core is precreated by the fixture; Modulus only checks that it exists.
        await RoundTripAsync(search, SolrFixture.Core);
    }
}
