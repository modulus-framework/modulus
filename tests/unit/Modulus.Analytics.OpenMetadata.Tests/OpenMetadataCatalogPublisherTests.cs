namespace Modulus.Analytics.OpenMetadata.Tests;

using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions.Entities;
using Xunit;

[Trait("Category", "Unit")]
public sealed class OpenMetadataCatalogPublisherTests
{
    private static readonly AnalyticsDatasetDescriptor Orders = new("orders", 2, "sales",
    [
        new("order_id", AnalyticsFieldType.Guid),
        new("total", AnalyticsFieldType.Decimal, IsNullable: true, MaxNullRate: 0.05),
        new("email", AnalyticsFieldType.String, IsNullable: true, Classification: FieldClassification.Restricted),
        new("placed_on", AnalyticsFieldType.Date),
    ],
    Description: "Placed orders");

    private static OpenMetadataCatalogPublisher Publisher(StubHandler handler, OpenMetadataOptions? options = null) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://openmetadata:8585/") }, options ?? new OpenMetadataOptions { ServiceName = "warehouse", DatabaseName = "analytics", SchemaName = "public" });

    [Fact]
    public async Task A_dataset_version_is_registered_as_a_table_under_the_configured_schema()
    {
        var handler = new StubHandler(StubHandler.Json(HttpStatusCode.Created));

        await Publisher(handler).PublishAsync(Orders, CancellationToken.None);

        var sent = handler.Sent.Should().ContainSingle().Subject;
        sent.Method.Should().Be(HttpMethod.Put, "PUT creates the table or updates it, so a rerun is safe");
        sent.Path.Should().Be("/api/v1/tables");
        using var json = JsonDocument.Parse(sent.Body);
        var root = json.RootElement;
        root.GetProperty("name").GetString().Should().Be("orders_v2");
        root.GetProperty("databaseSchema").GetString().Should().Be("warehouse.analytics.public");
        root.GetProperty("description").GetString().Should().Be("Placed orders");
        root.GetProperty("tableType").GetString().Should().Be("Regular");
    }

    [Fact]
    public async Task Each_field_becomes_a_column_with_its_type_and_its_nullability_and_classification_recorded()
    {
        var handler = new StubHandler(StubHandler.Json(HttpStatusCode.OK));

        await Publisher(handler).PublishAsync(Orders, CancellationToken.None);

        using var json = JsonDocument.Parse(handler.Sent.Single().Body);
        var columns = json.RootElement.GetProperty("columns").EnumerateArray().ToDictionary(c => c.GetProperty("name").GetString()!);
        columns["order_id"].GetProperty("dataType").GetString().Should().Be("UUID");
        columns["total"].GetProperty("dataType").GetString().Should().Be("DECIMAL");
        columns["total"].GetProperty("description").GetString().Should().Contain("May be null.").And.Contain("At most 5% null.");
        columns["email"].GetProperty("description").GetString().Should().Contain("Classification: Restricted.");
        columns["placed_on"].GetProperty("description").GetString().Should().Contain("Never null.");
    }

    [Theory]
    [InlineData(AnalyticsFieldType.String, "VARCHAR")]
    [InlineData(AnalyticsFieldType.Int64, "BIGINT")]
    [InlineData(AnalyticsFieldType.Double, "DOUBLE")]
    [InlineData(AnalyticsFieldType.Boolean, "BOOLEAN")]
    [InlineData(AnalyticsFieldType.Timestamp, "TIMESTAMP")]
    [InlineData(AnalyticsFieldType.Date, "DATE")]
    public void Every_field_type_maps_to_a_column_type(AnalyticsFieldType type, string expected)
        => OpenMetadataCatalogPublisher.ColumnTypeFor(type).Should().Be(expected);

    [Fact]
    public async Task A_refused_registration_is_reported_with_the_server_status()
    {
        var handler = new StubHandler(StubHandler.Json(HttpStatusCode.NotFound, "{\"message\":\"database schema not found\"}"));

        var act = () => Publisher(handler).PublishAsync(Orders, CancellationToken.None);

        var error = await act.Should().ThrowAsync<HttpRequestException>();
        error.Which.StatusCode.Should().Be(HttpStatusCode.NotFound);
        error.Which.Message.Should().Contain("orders_v2").And.Contain("schema not found");
    }

    [Fact]
    public async Task Publishing_a_catalog_registers_every_version()
    {
        var catalog = new AnalyticsDatasetCatalog();
        catalog.Add(new AnalyticsDatasetDescriptor("orders", 1, "sales", [new("order_id", AnalyticsFieldType.Guid)]));
        catalog.Add(Orders);
        var handler = new StubHandler(StubHandler.Json(HttpStatusCode.OK), StubHandler.Json(HttpStatusCode.OK));

        await Publisher(handler).PublishAllAsync(catalog, CancellationToken.None);

        handler.Sent.Select(s => JsonDocument.Parse(s.Body).RootElement.GetProperty("name").GetString())
            .Should().BeEquivalentTo(["orders_v1", "orders_v2"]);
    }

    [Fact]
    public async Task Registration_sends_the_bearer_token_and_validates_the_settings()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Analytics:OpenMetadata:BaseUrl"] = "http://openmetadata:8585",
            ["Analytics:OpenMetadata:ApiKey"] = "jwt-abc",
            ["Analytics:OpenMetadata:ServiceName"] = "warehouse",
        }).Build();
        var handler = new StubHandler(StubHandler.Json(HttpStatusCode.OK));
        var services = new ServiceCollection();
        services.AddModulusOpenMetadata(config);
        services.AddHttpClient<OpenMetadataCatalogPublisher>().ConfigurePrimaryHttpMessageHandler(() => handler);
        using var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<OpenMetadataCatalogPublisher>().PublishAsync(Orders, CancellationToken.None);

        handler.Sent.Single().Authorization.Should().Be("Bearer jwt-abc");
    }

    [Fact]
    public void A_missing_base_address_is_refused_at_configuration_time()
    {
        var services = new ServiceCollection();
        services.AddModulusOpenMetadata(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IOptions<OpenMetadataOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*BaseUrl*");
    }
}
