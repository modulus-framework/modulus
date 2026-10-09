namespace Modulus.GraphQL.Tests;

using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static GraphQLTestHost;

[Trait("Category", "Unit")]
public sealed class PersistedQueryTests
{
    private const string Read = GraphQLTestHost.ReadPolicy;
    private const string Query = "{ first }";

    [Fact]
    public async Task Allowlist_runs_a_registered_query_by_hash()
    {
        await using var host = await StartAsync(
            o => o.PersistedQueries.Mode = PersistedQueryMode.Allowlist,
            s => s.AddPersistedQueries(Query));

        var body = await ByHashAsync(host, PersistedQueryHash.Of(Query));

        body.TryGetProperty("errors", out _).Should().BeFalse(body.GetRawText());
        body.GetProperty("data").GetProperty("first").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Allowlist_refuses_query_text_and_unknown_hashes()
    {
        await using var host = await StartAsync(
            o => o.PersistedQueries.Mode = PersistedQueryMode.Allowlist,
            s => s.AddPersistedQueries(Query));

        var (_, text) = await PostAsync(host.Client(Read), Query);
        Code(text).Should().Be("PERSISTED_QUERY_REQUIRED");

        var unknown = await ByHashAsync(host, PersistedQueryHash.Of("{ second }"));
        Code(unknown).Should().Be("PERSISTED_QUERY_NOT_FOUND");
    }

    [Fact]
    public async Task Off_mode_accepts_any_query_and_still_resolves_hashes()
    {
        await using var host = await StartAsync(services: s => s.AddPersistedQueries(Query));

        var (_, text) = await PostAsync(host.Client(Read), "{ second }");
        text.TryGetProperty("errors", out _).Should().BeFalse(text.GetRawText());

        var body = await ByHashAsync(host, PersistedQueryHash.Of(Query));
        body.GetProperty("data").GetProperty("first").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task A_persisted_query_is_still_validated()
    {
        // MaxAliases defaults to 20, so lower it to prove the limits still run on stored text.
        await using var limited = await StartAsync(
            o =>
            {
                o.PersistedQueries.Mode = PersistedQueryMode.Allowlist;
                o.MaxAliases = 2;
            },
            s => s.AddPersistedQueries("{ a: first b: first c: first }"));

        var body = await ByHashAsync(limited, PersistedQueryHash.Of("{ a: first b: first c: first }"));

        body.GetProperty("errors")[0].GetProperty("message").GetString().Should().ContainEquivalentOf("aliases");
    }

    private static async Task<JsonElement> ByHashAsync(GraphQLTestHost host, string hash)
    {
        using var response = await host.Client(Read).PostAsJsonAsync("/graphql", new
        {
            extensions = new { persistedQuery = new { version = 1, sha256Hash = hash } },
        });
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private static string? Code(JsonElement body)
        => body.GetProperty("errors")[0].GetProperty("extensions").GetProperty("code").GetString();
}
