namespace Modulus.GraphQL.Tests;

using System.Net;
using System.Text.Json;
using FluentAssertions;
using global::GraphQL.Types;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using static GraphQLTestHost;

[Trait("Category", "Unit")]
public sealed class GraphQLServerTests
{
    private const string Read = GraphQLTestHost.ReadPolicy;
    private const string Manage = GraphQLTestHost.ManagePolicy;

    [Fact]
    public async Task Mutations_and_queries_go_through_the_mediator()
    {
        await using var host = await StartAsync();
        var client = host.Client(Read, Manage);

        var (status, created) = await PostAsync(client, "mutation($n: String!) { createProduct(name: $n) }", new { n = "Widget" });

        status.Should().Be(HttpStatusCode.OK);
        var id = created.GetProperty("data").GetProperty("createProduct").GetString();
        host.Store.Items.Should().ContainKey(Guid.Parse(id!));

        var (_, list) = await PostAsync(client, "{ products { id name } }");
        list.GetProperty("data").GetProperty("products")[0].GetProperty("name").GetString().Should().Be("Widget");

        var (_, one) = await PostAsync(client, "query($id: ID!) { product(id: $id) { name } }", new { id });
        one.GetProperty("data").GetProperty("product").GetProperty("name").GetString().Should().Be("Widget");
    }

    [Fact]
    public async Task An_anonymous_request_gets_401()
    {
        await using var host = await StartAsync();

        var (status, _) = await PostAsync(host.Client(), "{ products { id } }");

        status.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Fields_check_their_permission_policy()
    {
        await using var host = await StartAsync();

        var (status, body) = await PostAsync(host.Client(Read), "mutation { createProduct(name: \"x\") }");

        status.Should().Be(HttpStatusCode.Forbidden);
        Code(body).Should().Be("PERMISSION_DENIED");
        host.Store.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Without_the_authentication_requirement_an_anonymous_field_check_is_unauthenticated()
    {
        await using var host = await StartAsync(o => o.RequireAuthenticatedUser = false);

        var (status, body) = await PostAsync(host.Client(), "{ products { id } }");

        status.Should().Be(HttpStatusCode.Unauthorized);
        Code(body).Should().Be("UNAUTHENTICATED");
    }

    [Fact]
    public async Task Not_found_is_mapped_without_the_domain_message()
    {
        await using var host = await StartAsync();

        var (status, body) = await PostAsync(host.Client(Read), $"{{ product(id: \"{Guid.NewGuid()}\") {{ name }} }}");

        status.Should().Be(HttpStatusCode.OK);
        body.GetProperty("data").GetProperty("product").ValueKind.Should().Be(JsonValueKind.Null);
        var error = body.GetProperty("errors")[0];
        error.GetProperty("message").GetString().Should().Be("Resource not found");
        error.GetProperty("extensions").GetProperty("code").GetString().Should().Be("NOT_FOUND");
        error.GetProperty("path")[0].GetString().Should().Be("product");
    }

    [Fact]
    public async Task Validation_errors_carry_the_messages()
    {
        await using var host = await StartAsync();

        var (_, body) = await PostAsync(host.Client(Manage), "mutation { createProduct(name: \" \") }");

        var error = body.GetProperty("errors")[0];
        error.GetProperty("message").GetString().Should().Be("Validation failed");
        error.GetProperty("extensions").GetProperty("code").GetString().Should().Be("VALIDATION_FAILED");
        error.GetProperty("extensions").GetProperty("errors")[0].GetString().Should().Be("Name: must not be empty");
    }

    [Fact]
    public async Task A_malformed_id_is_a_validation_error()
    {
        await using var host = await StartAsync();

        var (_, body) = await PostAsync(host.Client(Read), "{ product(id: \"nope\") { name } }");

        Code(body).Should().Be("VALIDATION_FAILED");
    }

    [Theory]
    [InlineData("forbidden", "PERMISSION_DENIED", "Forbidden")]
    [InlineData("conflict", "CONFLICT", "Conflict")]
    [InlineData("feature", "FEATURE_DISABLED", "Feature not available")]
    [InlineData("unauthorized", "UNAUTHENTICATED", "Unauthorized")]
    [InlineData("boom", "INTERNAL", "An unexpected error occurred")]
    public async Task Exceptions_map_to_codes(string kind, string code, string message)
    {
        await using var host = await StartAsync();

        var (_, body) = await PostAsync(host.Client(Read), $"{{ fail(kind: \"{kind}\") }}");

        var error = body.GetProperty("errors")[0];
        error.GetProperty("message").GetString().Should().Be(message);
        error.GetProperty("extensions").GetProperty("code").GetString().Should().Be(code);
        body.GetRawText().Should().NotContain("secret connection string").And.NotContain("InvalidOperationException");
        if (kind == "feature")
            error.GetProperty("extensions").GetProperty("feature").GetString().Should().Be("Beta");
    }

    [Fact]
    public async Task Exception_details_are_exposed_only_when_enabled()
    {
        await using var host = await StartAsync(o => o.ExposeExceptionDetails = true);

        var (_, body) = await PostAsync(host.Client(Read), "{ fail(kind: \"boom\") }");

        body.GetRawText().Should().Contain("secret connection string");
        Code(body).Should().Be("INTERNAL");
    }

    [Fact]
    public async Task Introspection_is_off_unless_enabled()
    {
        await using (var off = await StartAsync())
        {
            var (status, body) = await PostAsync(off.Client(Read), "{ __schema { queryType { name } } }");
            status.Should().Be(HttpStatusCode.BadRequest);
            body.GetProperty("errors")[0].GetProperty("message").GetString().Should().ContainEquivalentOf("introspection");
            var (_, typename) = await PostAsync(off.Client(Read), "{ __typename }");
            typename.GetProperty("data").GetProperty("__typename").GetString().Should().Be("Query");
        }

        await using var on = await StartAsync(o => o.EnableIntrospection = true);
        var (_, schema) = await PostAsync(on.Client(Read), "{ __schema { queryType { name } mutationType { name } } }");
        schema.GetProperty("data").GetProperty("__schema").GetProperty("mutationType").GetProperty("name").GetString().Should().Be("Mutation");
    }

    [Fact]
    public async Task Depth_and_complexity_are_limited()
    {
        await using var host = await StartAsync(o =>
        {
            o.MaxDepth = 1;
            o.MaxComplexity = 3;
        });

        var (status, deep) = await PostAsync(host.Client(Read), "{ products { id } }");
        status.Should().Be(HttpStatusCode.BadRequest);
        deep.GetProperty("errors")[0].GetProperty("message").GetString().Should().ContainEquivalentOf("depth");

        var (_, flat) = await PostAsync(host.Client(Read), "{ a: first b: first c: first d: first }");
        flat.GetProperty("errors")[0].GetProperty("message").GetString().Should().ContainEquivalentOf("complex");
    }

    [Fact]
    public async Task Query_fields_resolve_one_at_a_time_so_scoped_services_are_safe()
    {
        await using var host = await StartAsync();

        var (_, body) = await PostAsync(host.Client(Read), "{ first second a: first b: second }");

        body.TryGetProperty("errors", out _).Should().BeFalse(body.GetRawText());
        body.GetProperty("data").GetProperty("b").GetInt32().Should().Be(4);
    }

    [Fact]
    public async Task Another_module_extends_a_type_and_its_field_is_batched()
    {
        await using var host = await StartAsync(services: s => s.AddInventoryGraphQL());
        var ids = Enumerable.Range(0, 3).Select(i =>
        {
            var id = Guid.NewGuid();
            host.Store.Items[id] = new ProductDto(id, $"p{i}");
            return id;
        }).ToList();

        var (_, body) = await PostAsync(host.Client(Read), "{ products { id stock } }");

        body.TryGetProperty("errors", out _).Should().BeFalse(body.GetRawText());
        foreach (var item in body.GetProperty("data").GetProperty("products").EnumerateArray())
            item.GetProperty("stock").GetInt32().Should().Be(InventoryGraphQL.StockOf(Guid.Parse(item.GetProperty("id").GetString()!)));
        var batches = host.App.Services.GetRequiredService<StockCounter>().Batches;
        batches.Should().ContainSingle().Which.Should().BeEquivalentTo(ids);
    }

    [Fact]
    public async Task Contributors_are_found_in_the_given_assemblies()
    {
        await using var host = await StartAsync(scanAssembly: true);
        host.Store.Items[Guid.NewGuid()] = new ProductDto(Guid.NewGuid(), "found");

        var (_, body) = await PostAsync(host.Client(Read), "{ products { name } }");

        body.GetProperty("data").GetProperty("products")[0].GetProperty("name").GetString().Should().Be("found");
    }

    [Fact]
    public async Task Get_and_form_posts_need_the_csrf_header()
    {
        await using var host = await StartAsync();
        var client = host.Client(Read);

        using var get = await client.GetAsync(new Uri("/graphql?query=%7B__typename%7D", UriKind.Relative));
        get.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        client.DefaultRequestHeaders.Add("GraphQL-Require-Preflight", "1");
        using var preflighted = await client.GetAsync(new Uri("/graphql?query=%7B__typename%7D", UriKind.Relative));
        preflighted.StatusCode.Should().Be(HttpStatusCode.OK);

        using var form = await client.PostAsync(new Uri("/graphql", UriKind.Relative),
            new FormUrlEncodedContent([new KeyValuePair<string, string>("query", "{ __typename }")]));
        form.IsSuccessStatusCode.Should().BeFalse();
    }

    [Fact]
    public async Task Batches_are_refused_unless_enabled()
    {
        await using var host = await StartAsync();

        using var response = await host.Client(Read).PostAsync(new Uri("/graphql", UriKind.Relative),
            new StringContent("[{\"query\":\"{ __typename }\"},{\"query\":\"{ __typename }\"}]", System.Text.Encoding.UTF8, "application/json"));

        response.IsSuccessStatusCode.Should().BeFalse();
    }

    [Fact]
    public async Task Without_mutation_fields_the_schema_has_no_mutation_type()
    {
        await using var host = await StartAsync(
            o => o.EnableIntrospection = true,
            services: s => s.RemoveAll<IGraphQLContributor>().AddSingleton<IGraphQLContributor>(new QueryOnly()));

        var (_, body) = await PostAsync(host.Client(Read), "{ __schema { mutationType { name } } }");

        body.GetProperty("data").GetProperty("__schema").GetProperty("mutationType").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_field_name_used_by_two_modules_fails_startup_naming_the_module()
    {
        var act = async () => await StartAsync(services: s => s.AddSingleton<IGraphQLContributor>(new Duplicate()));

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage($"*{typeof(Duplicate).FullName}*Query*products*");
    }

    [Fact]
    public async Task A_schema_without_query_fields_fails_startup()
    {
        var act = async () => await StartAsync(services: s => s.RemoveAll<IGraphQLContributor>());

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*no query field*");
    }

    [Fact]
    public async Task Mapping_without_registration_fails_clearly()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        await using var app = builder.Build();

        var act = () => app.MapModulusGraphQL();

        act.Should().Throw<InvalidOperationException>().WithMessage("*AddModulusGraphQL*");
    }

    [Fact]
    public async Task A_bff_can_host_it_on_a_client_route_group()
    {
        await using var host = await StartAsync(o => o.EnableUi = true, map: app => app.MapMobileGraphQL());
        var mobile = host.Client(Read);
        mobile.DefaultRequestHeaders.Add("X-Test-Claim", "client=mobile");

        var (denied, _) = await PostAsync(host.Client(Read), "{ __typename }", path: "/mobile/graphql");
        var (allowed, body) = await PostAsync(mobile, "{ __typename }", path: "/mobile/graphql");
        var (unmapped, _) = await PostAsync(mobile, "{ __typename }");

        denied.Should().Be(HttpStatusCode.Forbidden);
        allowed.Should().Be(HttpStatusCode.OK);
        body.GetProperty("data").GetProperty("__typename").GetString().Should().Be("Query");
        unmapped.Should().Be(HttpStatusCode.NotFound);

        var ui = await host.Client().GetStringAsync(new Uri("/mobile/graphql/ui", UriKind.Relative));
        ui.Should().Contain("../graphql");
    }

    [Fact]
    public async Task The_ui_is_not_mapped_by_default()
    {
        await using var host = await StartAsync();

        using var response = await host.Client(Read).GetAsync(new Uri("/graphql/ui", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static string? Code(JsonElement body)
        => body.GetProperty("errors")[0].GetProperty("extensions").GetProperty("code").GetString();

    private sealed class Duplicate : IGraphQLContributor
    {
        public void ConfigureQuery(ObjectGraphType query) => query.Field<StringGraphType>("products").Resolve(_ => "x");
    }

    private sealed class QueryOnly : IGraphQLContributor
    {
        public void ConfigureQuery(ObjectGraphType query) => query.Field<StringGraphType>("hello").Resolve(_ => "world");
    }
}
