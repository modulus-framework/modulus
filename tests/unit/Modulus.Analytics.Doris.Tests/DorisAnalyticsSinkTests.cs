namespace Modulus.Analytics.Doris.Tests;

using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

/// <summary>A real HTTP endpoint that plays the Doris frontend (which redirects Stream Load to a backend) and the backend that loads it.</summary>
internal sealed class DorisReceiver : IAsyncDisposable
{
    private readonly WebApplication _app;

    private DorisReceiver(WebApplication app) => _app = app;

    public ConcurrentQueue<(string Path, IDictionary<string, string> Headers, string? Authorization, string Body)> Loads { get; } = new();

    /// <summary>The status the backend answers with, and the body it returns.</summary>
    public int BackendStatus { get; set; } = StatusCodes.Status200OK;

    public string BackendBody { get; set; } = "{\"Status\":\"Success\",\"Message\":\"OK\"}";

    public Uri BaseUrl => new(_app.Urls.First());

    public static async Task<DorisReceiver> StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        var receiver = new DorisReceiver(app);

        // The frontend does not load: it redirects the PUT to a backend, as Doris does.
        app.MapMethods("/api/{db}/{table}/_stream_load", ["PUT"], (HttpContext context, string db, string table) =>
        {
            context.Response.StatusCode = StatusCodes.Status307TemporaryRedirect;
            context.Response.Headers.Location = $"/be/api/{db}/{table}/_stream_load";
        });

        app.MapMethods("/be/api/{db}/{table}/_stream_load", ["PUT"], async (HttpContext context, string db, string table) =>
        {
            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
            var body = await reader.ReadToEndAsync();
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in new[] { "format", "strip_outer_array", "label" })
            {
                headers[name] = context.Request.Headers[name].ToString();
            }

            receiver.Loads.Enqueue(($"/be/api/{db}/{table}/_stream_load", headers, context.Request.Headers.Authorization.ToString(), body));
            context.Response.StatusCode = receiver.BackendStatus;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(receiver.BackendBody);
        });

        await app.StartAsync();
        return receiver;
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();
}

public sealed class DorisAnalyticsSinkTests
{
    private static readonly Guid Tenant = Guid.Parse("8b9c0d1e-2f3a-4b5c-8d6e-7f8a9b0c1d2e");

    private static readonly AnalyticsDatasetDescriptor Orders = new("orders", 1, "sales",
    [
        new("order_id", AnalyticsFieldType.Int64),
        new("total", AnalyticsFieldType.Decimal),
        new("placed_on", AnalyticsFieldType.Date, IsNullable: true),
        new("placed_at", AnalyticsFieldType.Timestamp),
        new("paid", AnalyticsFieldType.Boolean),
    ]);

    private static readonly DateTimeOffset Now = new(2026, 11, 4, 10, 30, 0, 123, TimeSpan.Zero);

    private static ServiceProvider Build(Uri baseUrl, string? username = "doris_loader")
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Analytics:Doris:FeHttpUrl"] = baseUrl.ToString(),
            ["Analytics:Doris:Database"] = "analytics",
            ["Analytics:Doris:TablePrefix"] = "app_",
            ["Analytics:Doris:Username"] = username,
            ["Analytics:Doris:Password"] = username is null ? null : "doris-secret",
        }).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions<DorisSinkOptions>()
            .Bind(config.GetSection(DorisSinkOptions.SectionName));
        services.AddSingleton<IAnalyticsSink, DorisAnalyticsSink>();
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static AnalyticsRecord Record(long id, string total = "19.90") => new(
        AnalyticsRecordIds.Deterministic("orders", id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        "orders", 1, Tenant, new DateTimeOffset(2026, 11, 3, 9, 0, 0, TimeSpan.Zero),
        new Dictionary<string, object?>
        {
            ["order_id"] = id,
            ["total"] = decimal.Parse(total, System.Globalization.CultureInfo.InvariantCulture),
            ["placed_on"] = new DateOnly(2026, 11, 3),
            ["placed_at"] = new DateTimeOffset(2026, 11, 3, 9, 0, 0, 500, TimeSpan.Zero),
            ["paid"] = true,
        },
        Revision: 100 + id);

    [Fact]
    public async Task A_batch_is_loaded_once_through_the_frontend_redirect_with_typed_rows_and_a_content_label()
    {
        await using var doris = await DorisReceiver.StartAsync();
        await using var provider = Build(doris.BaseUrl);
        var sink = provider.GetRequiredService<IAnalyticsSink>();

        await sink.WriteAsync(Orders, [Record(2), Record(1)], CancellationToken.None);

        doris.Loads.TryDequeue(out var load).Should().BeTrue();
        load.Path.Should().Be("/be/api/analytics/app_orders_v1/_stream_load", "the frontend redirected the load to a backend");
        load.Headers["format"].Should().Be("json");
        load.Headers["strip_outer_array"].Should().Be("true");
        load.Headers["label"].Should().StartWith("modulus_orders_v1_");
        load.Authorization.Should().Be("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("doris_loader:doris-secret")));
        var rows = JsonDocument.Parse(load.Body).RootElement.EnumerateArray().ToArray();
        rows.Should().HaveCount(2);
        var first = rows.Single(r => r.GetProperty("order_id").GetInt64() == 1);
        first.GetProperty("tenant_id").GetString().Should().Be(Tenant.ToString("D"));
        first.GetProperty("revision").GetInt64().Should().Be(101);
        first.GetProperty("total").GetDecimal().Should().Be(19.90m);
        first.GetProperty("placed_on").GetString().Should().Be("2026-11-03");
        first.GetProperty("placed_at").GetString().Should().Be("2026-11-03 09:00:00.500000");
        first.GetProperty("paid").GetBoolean().Should().BeTrue();
        doris.Loads.Should().BeEmpty();
    }

    [Fact]
    public async Task The_same_batch_gets_the_same_label_and_an_already_loaded_batch_is_not_an_error()
    {
        await using var doris = await DorisReceiver.StartAsync();
        doris.BackendStatus = StatusCodes.Status200OK;
        doris.BackendBody = "{\"Status\":\"Label Already Exists\",\"ExistingJobStatus\":\"FINISHED\"}";
        await using var provider = Build(doris.BaseUrl);
        var sink = provider.GetRequiredService<IAnalyticsSink>();
        var batch = new[] { Record(1), Record(2) };

        var write = () => sink.WriteAsync(Orders, batch, CancellationToken.None);
        await write.Should().NotThrowAsync("the earlier attempt loaded this batch");
        await write.Should().NotThrowAsync();

        doris.Loads.Should().HaveCount(2);
        var labels = doris.Loads.Select(l => l.Headers["label"]).Distinct().ToArray();
        labels.Should().ContainSingle("a resent batch carries the same label, so Doris does not load it twice");
    }

    [Fact]
    public async Task A_refused_load_is_a_permanent_failure_with_the_reason()
    {
        await using var doris = await DorisReceiver.StartAsync();
        doris.BackendStatus = StatusCodes.Status200OK;
        doris.BackendBody = "{\"Status\":\"Fail\",\"Message\":\"no partition for the row\"}";
        await using var provider = Build(doris.BaseUrl);

        var write = () => provider.GetRequiredService<IAnalyticsSink>().WriteAsync(Orders, [Record(1)], CancellationToken.None);

        var error = await write.Should().ThrowAsync<AnalyticsSinkException>();
        error.Which.IsTransient.Should().BeFalse();
        error.Which.Message.Should().Contain("no partition for the row");
    }

    [Fact]
    public async Task A_server_error_from_doris_is_transient_so_the_batch_is_retried()
    {
        await using var doris = await DorisReceiver.StartAsync();
        doris.BackendStatus = StatusCodes.Status503ServiceUnavailable;
        doris.BackendBody = "{\"Status\":\"Fail\",\"Message\":\"backend busy\"}";
        await using var provider = Build(doris.BaseUrl);

        var write = () => provider.GetRequiredService<IAnalyticsSink>().WriteAsync(Orders, [Record(1)], CancellationToken.None);

        (await write.Should().ThrowAsync<AnalyticsSinkException>()).Which.IsTransient.Should().BeTrue();
    }

    [Fact]
    public async Task An_empty_batch_does_not_call_doris()
    {
        await using var doris = await DorisReceiver.StartAsync();
        await using var provider = Build(doris.BaseUrl);

        await provider.GetRequiredService<IAnalyticsSink>().WriteAsync(Orders, [], CancellationToken.None);

        doris.Loads.Should().BeEmpty();
    }

    [Fact]
    public void A_label_depends_only_on_the_records_so_a_different_batch_gets_a_different_label()
    {
        var one = DorisAnalyticsSink.BatchLabel(Orders, [Record(1)]);
        var two = DorisAnalyticsSink.BatchLabel(Orders, [Record(1), Record(2)]);
        var again = DorisAnalyticsSink.BatchLabel(Orders, [Record(1)]);

        again.Should().Be(one);
        two.Should().NotBe(one);
        one.Length.Should().BeLessThanOrEqualTo(128, "Doris limits a label to 128 characters");
    }

    [Fact]
    public void The_table_is_named_by_prefix_dataset_and_version()
    {
        DorisAnalyticsSink.TableName(new DorisSinkOptions { TablePrefix = "app_" }, Orders).Should().Be("app_orders_v1");
    }

    [Theory]
    [InlineData("Bad-Name")]
    [InlineData("1starts_with_digit")]
    public void A_database_that_is_not_an_identifier_fails_at_startup(string database)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Analytics:Doris:FeHttpUrl"] = "http://doris-fe:8030",
            ["Analytics:Doris:Database"] = database,
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions<DorisSinkOptions>().Bind(config.GetSection(DorisSinkOptions.SectionName))
            .Validate(o => DorisSinkOptions.IsIdentifier(o.Database), "bad database");
        using var provider = services.BuildServiceProvider();

        var read = () => provider.GetRequiredService<IOptions<DorisSinkOptions>>().Value;

        read.Should().Throw<OptionsValidationException>();
    }
}
