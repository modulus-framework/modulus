namespace Modulus.Analytics.EntityFrameworkCore.Tests;

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Analytics.Testing;
using Modulus.Core.Abstractions;
using Modulus.Tenancy.Lifecycle;
using Xunit;

// Offboarding and erasure of the analytics copies, on the shared harness. The handler runs in the company's tenant context, which the test sets.
public sealed partial class AnalyticsBackfillTests
{
    private readonly MovableTenant _current = new();

    private sealed class MovableTenant : ICurrentTenant
    {
        public Guid? TenantId { get; set; }

        public string? TenantSlug => null;

        public bool IsAvailable => TenantId is not null;

        public bool IsHost => TenantId is null;

        public IDisposable Change(TenantInfo? tenant) => new NoOp();

        private sealed class NoOp : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }

    /// <summary>A sink that cannot delete one company's rows, like a lake of files with no listing.</summary>
    private sealed class NonErasableSink(string name) : IAnalyticsSink
    {
        public string Name { get; } = name;

        public Task EnsureDatasetAsync(AnalyticsDatasetDescriptor dataset, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task WriteAsync(AnalyticsDatasetDescriptor dataset, IReadOnlyList<AnalyticsRecord> records, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>Counts (or deletes) what the handler holds for <paramref name="tenant"/> in one of its datasets, inside that tenant's context.</summary>
    private Task<long> Handle(Guid? tenant, string dataset, bool delete)
    {
        _current.TenantId = tenant;
        return InScope(sp =>
        {
            var handler = ActivatorUtilities.CreateInstance<AnalyticsTenantDataHandler>(sp);
            var owned = new TenantDataset(dataset, TenantDataCategory.Owned);
            return delete
                ? handler.DeleteAsync(owned, CancellationToken.None)
                : handler.CountAsync(owned, CancellationToken.None);
        });
    }

    [Fact]
    public async Task Offboarding_erases_one_company_from_every_analytics_copy_and_leaves_the_others_alone()
    {
        Build(extra: s => s.AddSingleton<ICurrentTenant>(_current));
        await SeedOrdersAsync(_tenantA, 2);
        await SeedOrdersAsync(_tenantB, 1, firstId: 10);
        await Start([_tenantA, _tenantB]);
        await DrainRunsAsync();
        await DrainDeliveriesAsync();

        (await Handle(_tenantA, AnalyticsTenantDataHandler.WarehouseDataset, delete: false)).Should().Be(2);
        (await Handle(_tenantB, AnalyticsTenantDataHandler.WarehouseDataset, delete: false)).Should().Be(1);
        (await Handle(_tenantA, AnalyticsTenantDataHandler.BackfillDataset, delete: false)).Should().Be(1);
        (await Handle(_tenantA, AnalyticsTenantDataHandler.QueueDataset, delete: false)).Should().BeGreaterThan(0);

        (await Handle(_tenantA, AnalyticsTenantDataHandler.WarehouseDataset, delete: true)).Should().Be(2);
        (await Handle(_tenantA, AnalyticsTenantDataHandler.QueueDataset, delete: true)).Should().BeGreaterThan(0);
        (await Handle(_tenantA, AnalyticsTenantDataHandler.BackfillDataset, delete: true)).Should().Be(1);

        foreach (var dataset in new[] { AnalyticsTenantDataHandler.QueueDataset, AnalyticsTenantDataHandler.BackfillDataset, AnalyticsTenantDataHandler.WarehouseDataset })
        {
            (await Handle(_tenantA, dataset, delete: false)).Should().Be(0, $"{dataset} of the offboarded company is gone");
        }

        _sink.Rows.Count(r => r.TenantId == _tenantA).Should().Be(0);
        _sink.Rows.Count(r => r.TenantId == _tenantB).Should().Be(1, "another company's copies are untouched");
        (await Handle(_tenantB, AnalyticsTenantDataHandler.WarehouseDataset, delete: false)).Should().Be(1);
        (await Handle(_tenantB, AnalyticsTenantDataHandler.BackfillDataset, delete: false)).Should().Be(1);
    }

    [Fact]
    public async Task Erasing_twice_removes_nothing_the_second_time()
    {
        Build(extra: s => s.AddSingleton<ICurrentTenant>(_current));
        await SeedOrdersAsync(_tenantA, 2);
        await Start([_tenantA]);
        await DrainRunsAsync();
        await DrainDeliveriesAsync();

        await Handle(_tenantA, AnalyticsTenantDataHandler.WarehouseDataset, delete: true);
        await Handle(_tenantA, AnalyticsTenantDataHandler.BackfillDataset, delete: true);
        await Handle(_tenantA, AnalyticsTenantDataHandler.QueueDataset, delete: true);

        (await Handle(_tenantA, AnalyticsTenantDataHandler.WarehouseDataset, delete: true)).Should().Be(0);
        (await Handle(_tenantA, AnalyticsTenantDataHandler.BackfillDataset, delete: true)).Should().Be(0);
        (await Handle(_tenantA, AnalyticsTenantDataHandler.QueueDataset, delete: true)).Should().Be(0);
    }

    [Fact]
    public async Task A_company_is_not_reported_as_erased_while_a_sink_cannot_delete_its_rows()
    {
        Build(extra: s =>
        {
            s.AddSingleton<ICurrentTenant>(_current);
            s.AddSingleton<IAnalyticsSink>(new NonErasableSink("lake"));
        });
        await SeedOrdersAsync(_tenantA, 1);
        await Start([_tenantA]);
        await DrainRunsAsync();
        await DrainDeliveriesAsync();

        var erase = () => Handle(_tenantA, AnalyticsTenantDataHandler.WarehouseDataset, delete: true);
        var count = () => Handle(_tenantA, AnalyticsTenantDataHandler.WarehouseDataset, delete: false);

        await erase.Should().ThrowAsync<InvalidOperationException>().WithMessage("*lake*cannot delete*");
        await count.Should().ThrowAsync<InvalidOperationException>().WithMessage("*lake*");
        (await Handle(_tenantA, AnalyticsTenantDataHandler.QueueDataset, delete: false)).Should().BeGreaterThan(0, "a refused erasure removes nothing");
    }

    [Fact]
    public async Task The_handler_refuses_to_run_outside_a_company_context()
    {
        Build(extra: s => s.AddSingleton<ICurrentTenant>(_current));

        var act = () => Handle(null, AnalyticsTenantDataHandler.QueueDataset, delete: true);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*tenant context*");
    }

    [Fact]
    public async Task An_erasable_sink_counts_zero_for_a_dataset_version_it_never_created()
    {
        var sink = new InMemoryAnalyticsSink();
        var dataset = new AnalyticsDatasetDescriptor("never_written", 1, "sales", [new("x", AnalyticsFieldType.Int64)]);

        (await sink.CountTenantAsync(dataset, _tenantA, CancellationToken.None)).Should().Be(0);
        (await sink.EraseTenantAsync(dataset, _tenantA, CancellationToken.None)).Should().Be(0);
    }
}
