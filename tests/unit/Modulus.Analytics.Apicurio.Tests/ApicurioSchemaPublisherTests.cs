namespace Modulus.Analytics.Apicurio.Tests;

using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

[Trait("Category", "Unit")]
public sealed class ApicurioSchemaPublisherTests
{
    private static readonly AnalyticsDatasetDescriptor Orders = new("orders", 1, "sales",
        [new("order_id", AnalyticsFieldType.Guid), new("total", AnalyticsFieldType.Decimal, IsNullable: true)],
        Description: "Placed orders");

    private static ApicurioSchemaPublisher Publisher(StubHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://apicurio:8080/") });

    [Fact]
    public async Task A_new_dataset_version_becomes_a_new_json_schema_artifact()
    {
        var handler = new StubHandler(StubHandler.Json(HttpStatusCode.OK));

        var result = await Publisher(handler).PublishAsync(Orders, "modulus-analytics", CancellationToken.None);

        result.Should().Be(new ApicurioPublishResult("orders-v1", Created: true));
        var sent = handler.Sent.Should().ContainSingle().Subject;
        sent.Method.Should().Be(HttpMethod.Post);
        sent.Path.Should().Be("/apis/registry/v3/groups/modulus-analytics/artifacts");
        using var json = JsonDocument.Parse(sent.Body);
        json.RootElement.GetProperty("artifactId").GetString().Should().Be("orders-v1");
        json.RootElement.GetProperty("artifactType").GetString().Should().Be("JSON");
        json.RootElement.GetProperty("description").GetString().Should().Be("Placed orders");
        var content = json.RootElement.GetProperty("firstVersion").GetProperty("content");
        content.GetProperty("contentType").GetString().Should().Be("application/json");
        content.GetProperty("content").GetString().Should().Contain("\"$schema\"").And.Contain("orders.v1");
    }

    [Fact]
    public async Task An_existing_artifact_gets_the_schema_as_a_new_version()
    {
        var handler = new StubHandler(StubHandler.Json(HttpStatusCode.Conflict), StubHandler.Json(HttpStatusCode.OK));

        var result = await Publisher(handler).PublishAsync(Orders, "modulus-analytics", CancellationToken.None);

        result.Created.Should().BeFalse();
        handler.Sent.Should().HaveCount(2);
        handler.Sent[1].Method.Should().Be(HttpMethod.Post);
        handler.Sent[1].Path.Should().Be("/apis/registry/v3/groups/modulus-analytics/artifacts/orders-v1/versions");
        using var json = JsonDocument.Parse(handler.Sent[1].Body);
        json.RootElement.GetProperty("content").GetProperty("content").GetString().Should().Contain("orders.v1");
    }

    [Fact]
    public async Task A_refused_request_is_reported_with_the_registry_status()
    {
        var handler = new StubHandler(StubHandler.Json(HttpStatusCode.Forbidden, "{\"message\":\"no write access\"}"));

        var act = () => Publisher(handler).PublishAsync(Orders, "modulus-analytics", CancellationToken.None);

        var error = await act.Should().ThrowAsync<HttpRequestException>();
        error.Which.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        error.Which.Message.Should().Contain("create artifact 'orders-v1'").And.Contain("no write access");
    }

    [Fact]
    public async Task Publishing_a_catalog_publishes_every_version()
    {
        var catalog = new AnalyticsDatasetCatalog();
        catalog.Add(Orders);
        catalog.Add(new AnalyticsDatasetDescriptor("orders", 2, "sales", [new("order_id", AnalyticsFieldType.Guid)]));
        var handler = new StubHandler(StubHandler.Json(HttpStatusCode.OK), StubHandler.Json(HttpStatusCode.OK));

        var results = await Publisher(handler).PublishAllAsync(catalog, "modulus-analytics", CancellationToken.None);

        results.Select(r => r.ArtifactId).Should().BeEquivalentTo(["orders-v1", "orders-v2"]);
    }

    [Fact]
    public async Task Registration_sends_the_bearer_token_and_validates_the_base_address()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Analytics:Apicurio:BaseUrl"] = "http://apicurio:8080",
            ["Analytics:Apicurio:ApiKey"] = "token-123",
        }).Build();
        var handler = new StubHandler(StubHandler.Json(HttpStatusCode.OK));
        var services = new ServiceCollection();
        services.AddModulusApicurio(config);
        services.AddHttpClient<ApicurioSchemaPublisher>().ConfigurePrimaryHttpMessageHandler(() => handler);
        using var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<ApicurioSchemaPublisher>().PublishAsync(Orders, "modulus-analytics", CancellationToken.None);

        handler.Sent.Single().Authorization.Should().Be("Bearer token-123");
    }

    [Fact]
    public void A_missing_base_address_is_refused_at_configuration_time()
    {
        var services = new ServiceCollection();
        services.AddModulusApicurio(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IOptions<ApicurioOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*BaseUrl*");
    }
}
