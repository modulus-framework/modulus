namespace Modulus.Analytics.Tests;

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Analytics.Testing;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.DataProtection;
using Modulus.Core.Abstractions.Entities;
using Modulus.Events;
using Modulus.Events.Abstractions;
using Xunit;

[Trait("Category", "Unit")]
public sealed class AnalyticsCaptureTests
{
    public sealed record OrderPlaced(Guid OrderId, decimal Total, string Email) : IntegrationEventBase("test.order-placed.v1");

    private sealed class FakeProtector : IPersonalDataProtector
    {
        public string Protect(string plaintext) => plaintext;

        public string Unprotect(string ciphertext) => ciphertext;

        public string Hash(string value) => "h:" + value;
    }

    private sealed class FixedTenant(Guid? id) : ICurrentTenant
    {
        public Guid? TenantId => id;

        public string? TenantSlug => id?.ToString();

        public bool IsAvailable => id is not null;

        public bool IsHost => id is null;

        public IDisposable Change(TenantInfo? tenant) => throw new NotSupportedException();
    }

    private static readonly AnalyticsDatasetDescriptor Orders = new("orders", 1, "sales",
    [
        new("order_id", AnalyticsFieldType.Guid),
        new("total", AnalyticsFieldType.Decimal),
        new("email", AnalyticsFieldType.String, Classification: FieldClassification.Confidential),
        new("placed_on", AnalyticsFieldType.Date, IsNullable: true),
        new("placed_at", AnalyticsFieldType.Timestamp, IsNullable: true),
    ]);

    private static ServiceProvider Build(InMemoryAnalyticsSink sink, Guid? tenant, Action<AnalyticsBuilder>? extra = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IPersonalDataProtector>(new FakeProtector());
        services.AddSingleton<ICurrentTenant>(new FixedTenant(tenant));
        services.AddSingleton<IAnalyticsSink>(sink);
        services.AddModulusAnalytics(a =>
        {
            a.AddDataset(Orders);
            a.MapEvent<OrderPlaced>("orders", 1, e => new Dictionary<string, object?>
            {
                ["order_id"] = e.OrderId,
                ["total"] = e.Total,
                ["email"] = e.Email,
            });
            extra?.Invoke(a);
        });
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task A_captured_event_reaches_the_sink_masked_with_its_event_id_and_tenant()
    {
        var sink = new InMemoryAnalyticsSink();
        var tenant = Guid.NewGuid();
        using var provider = Build(sink, tenant);
        using var scope = provider.CreateScope();
        var @event = new OrderPlaced(Guid.NewGuid(), 12.5m, "a@b.c");

        await scope.ServiceProvider.GetRequiredService<IIntegrationEventHandler<OrderPlaced>>().HandleAsync(@event, CancellationToken.None);

        var row = sink.Rows.Should().ContainSingle().Subject;
        row.EventId.Should().Be(@event.EventId);
        row.TenantId.Should().Be(tenant);
        row.Values["email"].Should().Be("h:a@b.c");
        row.Values["total"].Should().Be(12.5m);
    }

    [Fact]
    public async Task Redelivering_the_same_event_does_not_duplicate_the_row()
    {
        var sink = new InMemoryAnalyticsSink();
        using var provider = Build(sink, Guid.NewGuid());
        using var scope = provider.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<IIntegrationEventHandler<OrderPlaced>>();
        var @event = new OrderPlaced(Guid.NewGuid(), 1m, "x");

        await handler.HandleAsync(@event, CancellationToken.None);
        await handler.HandleAsync(@event, CancellationToken.None);

        sink.Rows.Should().ContainSingle();
    }

    [Fact]
    public async Task An_event_without_a_tenant_fails_delivery_so_the_pipeline_retries_it()
    {
        var sink = new InMemoryAnalyticsSink();
        using var provider = Build(sink, tenant: null);
        using var scope = provider.CreateScope();

        var act = () => scope.ServiceProvider.GetRequiredService<IIntegrationEventHandler<OrderPlaced>>()
            .HandleAsync(new OrderPlaced(Guid.NewGuid(), 1m, "x"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*tenant*");
        sink.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task A_failing_sink_makes_the_inline_queue_throw()
    {
        var sink = new InMemoryAnalyticsSink { FailWith = new AnalyticsSinkException("down", isTransient: true) };
        using var provider = Build(sink, Guid.NewGuid());
        using var scope = provider.CreateScope();

        var act = () => scope.ServiceProvider.GetRequiredService<IIntegrationEventHandler<OrderPlaced>>()
            .HandleAsync(new OrderPlaced(Guid.NewGuid(), 1m, "x"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*down*");
    }

    [Fact]
    public async Task A_mapping_to_an_unregistered_dataset_version_fails_at_capture()
    {
        var sink = new InMemoryAnalyticsSink();
        var services = new ServiceCollection();
        services.AddSingleton<IPersonalDataProtector>(new FakeProtector());
        services.AddSingleton<IAnalyticsSink>(sink);
        services.AddModulusAnalytics(a => a.MapEvent<OrderPlaced>("orders", 7, _ => new Dictionary<string, object?>()));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var act = () => scope.ServiceProvider.GetRequiredService<IIntegrationEventHandler<OrderPlaced>>()
            .HandleAsync(new OrderPlaced(Guid.NewGuid(), 1m, "x"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not registered*");
    }

    [Fact]
    public async Task A_mapper_that_omits_a_required_field_fails_at_capture()
    {
        var sink = new InMemoryAnalyticsSink();
        var services = new ServiceCollection();
        services.AddSingleton<IPersonalDataProtector>(new FakeProtector());
        services.AddSingleton<ICurrentTenant>(new FixedTenant(Guid.NewGuid()));
        services.AddSingleton<IAnalyticsSink>(sink);
        services.AddModulusAnalytics(a => a.AddDataset(Orders)
            .MapEvent<OrderPlaced>("orders", 1, e => new Dictionary<string, object?> { ["order_id"] = e.OrderId, ["total"] = e.Total }));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var act = () => scope.ServiceProvider.GetRequiredService<IIntegrationEventHandler<OrderPlaced>>()
            .HandleAsync(new OrderPlaced(Guid.NewGuid(), 1m, "x"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*'email'*required*");
    }

    [Fact]
    public void Mapping_an_event_registers_it_for_broker_consumers()
    {
        var services = new ServiceCollection();
        services.AddModulusAnalytics(a => a.AddDataset(Orders).MapEvent<OrderPlaced>("orders", 1, _ => new Dictionary<string, object?>()));
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IIntegrationEventRegistry>().TryGetType(IntegrationEventNaming.GetName(typeof(OrderPlaced)), out var type).Should().BeTrue();
        type.Should().Be(typeof(OrderPlaced));
    }

    [Fact]
    public void Serializer_round_trips_every_field_type_with_its_clr_type()
    {
        var dataset = new AnalyticsDatasetDescriptor("all", 1, "o",
        [
            new("s", AnalyticsFieldType.String),
            new("i", AnalyticsFieldType.Int64),
            new("m", AnalyticsFieldType.Decimal),
            new("d", AnalyticsFieldType.Double),
            new("b", AnalyticsFieldType.Boolean),
            new("ts", AnalyticsFieldType.Timestamp),
            new("dt", AnalyticsFieldType.Date),
            new("g", AnalyticsFieldType.Guid),
            new("n", AnalyticsFieldType.String, IsNullable: true),
        ]);
        var guid = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 11, 3, 10, 30, 0, TimeSpan.FromHours(2));
        var values = new Dictionary<string, object?>
        {
            ["s"] = "x",
            ["i"] = 5,
            ["m"] = 1.25m,
            ["d"] = 2.5,
            ["b"] = true,
            ["ts"] = at,
            ["dt"] = new DateOnly(2026, 11, 3),
            ["g"] = guid,
            ["n"] = null,
            ["extra"] = "dropped",
        };

        var back = AnalyticsValueSerializer.Deserialize(dataset, AnalyticsValueSerializer.Serialize(dataset, values));

        back["s"].Should().Be("x");
        back["i"].Should().Be(5L);
        back["m"].Should().Be(1.25m);
        back["d"].Should().Be(2.5);
        back["b"].Should().Be(true);
        back["ts"].Should().Be(at.ToUniversalTime());
        back["dt"].Should().Be(new DateOnly(2026, 11, 3));
        back["g"].Should().Be(guid);
        back["n"].Should().BeNull();
        back.Should().NotContainKey("extra");
    }

    [Fact]
    public void Serializer_rejects_a_value_of_the_wrong_type_and_corrupt_json()
    {
        var dataset = new AnalyticsDatasetDescriptor("d", 1, "o", [new("n", AnalyticsFieldType.Int64)]);

        var wrong = () => AnalyticsValueSerializer.Serialize(dataset, new Dictionary<string, object?> { ["n"] = "abc" });
        var corrupt = () => AnalyticsValueSerializer.Deserialize(dataset, "{\"n\":\"abc\"}");

        wrong.Should().Throw<InvalidOperationException>().WithMessage("*'n'*Int64*");
        corrupt.Should().Throw<InvalidOperationException>().WithMessage("*do not match*");
    }
}
