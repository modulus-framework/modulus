namespace Modulus.AI.Connector.Tests;

using System.Net;
using System.Text.Json;
using FluentAssertions;
using Modulus.Core.Abstractions;
using Xunit;

[Trait("Category", "Unit")]
public sealed class ConnectorExecutionTests
{
    private const string Search = "/capabilities/Test.Catalog.Product.Search:execute";

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    [Fact]
    public async Task Manifest_lists_capabilities_and_classifies_fields()
    {
        await using var host = await ConnectorTestHost.StartAsync();

        var manifest = await JsonOf(await host.Client.SendAsync(host.Request(HttpMethod.Get, "/manifest", null)));

        manifest.GetProperty("contractVersion").GetString().Should().Be("1");
        manifest.GetProperty("fingerprint").GetString().Should().HaveLength(32);
        var capabilities = manifest.GetProperty("capabilities").EnumerateArray().ToList();
        capabilities.Select(c => c.GetProperty("name").GetString())
            .Should().Equal("Test.Catalog.Product.Count", "Test.Catalog.Product.Search");

        var search = capabilities.Single(c => c.GetProperty("name").GetString() == "Test.Catalog.Product.Search");
        search.GetProperty("readOnly").GetBoolean().Should().BeTrue();
        search.GetProperty("requiredPermissions").EnumerateArray().Select(p => p.GetString()).Should().Equal("catalog:read");
        search.GetProperty("inputSchema").GetProperty("properties").TryGetProperty("text", out _).Should().BeTrue();

        var fields = search.GetProperty("outputFields").EnumerateArray()
            .ToDictionary(f => f.GetProperty("name").GetString()!, f => f.GetProperty("classification").GetString());
        fields.Should().NotContainKey("apiToken");
        fields["name"].Should().Be("Confidential"); // unclassified: the configured default
        fields["cost"].Should().Be("Confidential");
        fields["owner"].Should().Be("Restricted"); // personal data

        var resource = manifest.GetProperty("resourceTypes").EnumerateArray().Single();
        resource.GetProperty("type").GetString().Should().Be("Catalog.Product");
        resource.GetProperty("titleField").GetString().Should().Be("name");
    }

    [Fact]
    public async Task Fingerprint_is_stable()
    {
        await using var first = await ConnectorTestHost.StartAsync();
        await using var second = await ConnectorTestHost.StartAsync();

        var a = await JsonOf(await first.Client.SendAsync(first.Request(HttpMethod.Get, "/manifest", null)));
        var b = await JsonOf(await second.Client.SendAsync(second.Request(HttpMethod.Get, "/manifest", null)));

        a.GetProperty("fingerprint").GetString().Should().Be(b.GetProperty("fingerprint").GetString());
    }

    [Fact]
    public async Task Health_reports_the_contract_version()
    {
        await using var host = await ConnectorTestHost.StartAsync();

        var health = await JsonOf(await host.Client.SendAsync(host.Request(HttpMethod.Get, "/health", null)));

        health.GetProperty("status").GetString().Should().Be("ok");
    }

    [Fact]
    public async Task Execute_runs_as_the_user_and_drops_secret_and_masked_fields()
    {
        await using var host = await ConnectorTestHost.StartAsync();

        var response = await host.PostAsync(Search, """{"args":{"text":"widg"}}""");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await JsonOf(response);
        result.GetProperty("truncated").GetBoolean().Should().BeFalse();
        var record = result.GetProperty("resources").EnumerateArray().Single();
        record.GetProperty("reference").GetProperty("resourceId").GetString().Should().Be(Catalog.WidgetId.ToString("D"));
        record.GetProperty("deepLink").GetString().Should().Be($"https://erp.test/products/{Catalog.WidgetId:D}");

        var fields = record.GetProperty("fields");
        fields.GetProperty("name").GetString().Should().Be("Widget");
        fields.TryGetProperty("apiToken", out _).Should().BeFalse("secret fields are never sent");
        fields.TryGetProperty("cost", out _).Should().BeFalse("a confidential field without clearance is masked");
        var supplier = fields.GetProperty("supplier");
        supplier.GetProperty("name").GetString().Should().Be("Acme");
        supplier.TryGetProperty("margin", out _).Should().BeFalse("nested fields are masked too");

        host.Audit.Events.Should().Contain(e => e.Category == SecurityAuditCategories.Ai
            && e.Action == "connector.capability" && e.Outcome == SecurityAuditOutcomes.Success
            && e.Actor == TestUsers.Reader.ToString("D") && e.CorrelationId == "corr-1");
    }

    [Fact]
    public async Task A_scalar_capability_returns_a_value_field()
    {
        await using var host = await ConnectorTestHost.StartAsync();

        var response = await host.PostAsync("/capabilities/Test.Catalog.Product.Count:execute", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var record = (await JsonOf(response)).GetProperty("resources").EnumerateArray().Single();
        record.GetProperty("fields").GetProperty("value").GetInt32().Should().Be(Catalog.Products.Count);
    }

    [Fact]
    public async Task Results_are_capped_and_marked_truncated()
    {
        await using var host = await ConnectorTestHost.StartAsync(s => s["Ai:Connector:MaxResults"] = "1");

        var result = await JsonOf(await host.PostAsync(Search, "{}"));

        result.GetProperty("resources").GetArrayLength().Should().Be(1);
        result.GetProperty("truncated").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task A_user_without_the_permission_is_denied()
    {
        await using var host = await ConnectorTestHost.StartAsync();

        var response = await host.PostAsync(Search, "{}", host.Envelope(TestUsers.NoRights));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await JsonOf(response)).GetProperty("code").GetString().Should().Be("DENIED");
        host.Audit.Events.Should().Contain(e => e.Action == "connector.capability" && e.Outcome == SecurityAuditOutcomes.Denied);
    }

    [Fact]
    public async Task An_unknown_capability_is_not_found()
    {
        await using var host = await ConnectorTestHost.StartAsync();

        var response = await host.PostAsync("/capabilities/Test.Nope.Thing.Do:execute", "{}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await JsonOf(response)).GetProperty("code").GetString().Should().Be("NOT_FOUND");
    }

    [Theory]
    [InlineData("""{"args":{"unknown":1}}""")]
    [InlineData("""{"args":[1]}""")]
    [InlineData("""{"args":{"text":5}}""")]
    [InlineData("""{not json""")]
    public async Task Bad_arguments_are_an_invalid_request(string body)
    {
        await using var host = await ConnectorTestHost.StartAsync();

        var response = await host.PostAsync(Search, body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await JsonOf(response)).GetProperty("code").GetString().Should().Be("INVALID_REQUEST");
    }

    [Fact]
    public async Task Resources_get_returns_one_record_or_not_found()
    {
        await using var host = await ConnectorTestHost.StartAsync();

        var found = await host.PostAsync("/resources:get", $$"""{"resourceType":"Catalog.Product","resourceId":"{{Catalog.GadgetId}}"}""");
        var missing = await host.PostAsync("/resources:get", $$"""{"resourceType":"Catalog.Product","resourceId":"{{Guid.NewGuid()}}"}""");
        var malformedId = await host.PostAsync("/resources:get", """{"resourceType":"Catalog.Product","resourceId":"x"}""");

        found.StatusCode.Should().Be(HttpStatusCode.OK);
        (await JsonOf(found)).GetProperty("fields").GetProperty("name").GetString().Should().Be("Gadget");
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        malformedId.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Scope_reports_permissions_field_policies_and_the_revocation_key()
    {
        await using var host = await ConnectorTestHost.StartAsync();
        host.Memberships.Add(TestUsers.Reader, TestTenantRestorer.Company);

        var response = await host.PostAsync("/authz/scope", null, host.Envelope(instance: ConnectorTestHost.CompanyInstance));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var scope = await JsonOf(response);
        scope.GetProperty("appInstanceId").GetString().Should().Be(ConnectorTestHost.CompanyInstance);
        scope.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()).Should().Equal("catalog:read");
        scope.GetProperty("roles").EnumerateArray().Select(p => p.GetString()).Should().Equal("reader");
        scope.GetProperty("dataScopes").GetProperty("company").EnumerateArray().Single().GetString()
            .Should().Be(TestTenantRestorer.Company.ToString("D"));
        var policies = scope.GetProperty("fieldPolicies");
        policies.GetProperty("Catalog.Product.name").GetString().Should().Be("Allow");
        policies.GetProperty("Catalog.Product.cost").GetString().Should().Be("Deny");
        policies.TryGetProperty("Catalog.Product.apiToken", out _).Should().BeFalse();
        scope.GetProperty("ttlSeconds").GetInt32().Should().Be(300);
        scope.GetProperty("revocationKey").GetString().Should().Be($"modulus:{ConnectorTestHost.CompanyInstance}");
    }

    [Fact]
    public async Task Resources_check_decides_each_record_in_order()
    {
        await using var host = await ConnectorTestHost.StartAsync();
        var body = $$"""
            {"resources":[
              {"resourceType":"Catalog.Product","resourceId":"{{Catalog.WidgetId}}"},
              {"resourceType":"Catalog.Product","resourceId":"{{Guid.NewGuid()}}"},
              {"resourceType":"Nope.Type","resourceId":"1"}]}
            """;

        var allowed = (await JsonOf(await host.PostAsync("/authz/resources:check", body)))
            .GetProperty("decisions").EnumerateArray().Select(d => d.GetProperty("allowed").GetBoolean()).ToList();
        var denied = (await JsonOf(await host.PostAsync("/authz/resources:check", body, host.Envelope(TestUsers.NoRights))))
            .GetProperty("decisions").EnumerateArray().Select(d => d.GetProperty("allowed").GetBoolean()).ToList();

        allowed.Should().Equal(true, false, false);
        denied.Should().Equal(false, false, false);
    }

    [Fact]
    public async Task Resources_check_caps_the_batch()
    {
        await using var host = await ConnectorTestHost.StartAsync(s => s["Ai:Connector:MaxBatchSize"] = "1");
        var body = """{"resources":[{"resourceType":"Catalog.Product","resourceId":"1"},{"resourceType":"Catalog.Product","resourceId":"2"}]}""";

        var response = await host.PostAsync("/authz/resources:check", body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Fields_check_returns_only_readable_fields()
    {
        await using var host = await ConnectorTestHost.StartAsync();
        var body = $$"""{"resource":{"resourceType":"Catalog.Product","resourceId":"{{Catalog.WidgetId}}"},"fields":["name","cost","apiToken","nope"]}""";

        var result = await JsonOf(await host.PostAsync("/authz/fields:check", body));

        result.GetProperty("allowedFields").EnumerateArray().Select(f => f.GetString()).Should().Equal("name");
    }
}
