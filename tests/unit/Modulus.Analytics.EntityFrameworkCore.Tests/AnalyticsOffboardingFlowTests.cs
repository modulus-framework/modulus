namespace Modulus.Analytics.EntityFrameworkCore.Tests;

using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;
using Modulus.MultiTenancy;
using Modulus.Storage;
using Modulus.Tenancy.Lifecycle;
using Xunit;

// Offboarding end to end: the real TenantLifecycleProvider drives the analytics step and handler, on the same harness as the unit tests above.
public sealed partial class AnalyticsBackfillTests
{
    /// <summary>The ambient company with real <c>Change</c> semantics, one per scope, as the provider enters each company in its own scope.</summary>
    private sealed class ScopedTenant : ICurrentTenant
    {
        private readonly Stack<(Guid? Id, bool Host)> _previous = new();

        public Guid? TenantId { get; private set; }

        public bool IsHost { get; private set; } = true;

        public string? TenantSlug => null;

        public bool IsAvailable => TenantId is not null;

        public IDisposable Change(TenantInfo? tenant)
        {
            _previous.Push((TenantId, IsHost));
            TenantId = tenant?.TenantId;
            IsHost = tenant is null;
            return new Restore(this);
        }

        private sealed class Restore(ScopedTenant owner) : IDisposable
        {
            public void Dispose() => (owner.TenantId, owner.IsHost) = owner._previous.Pop();
        }
    }

    private sealed class RecordingStatuses : ITenantStatusWriter
    {
        public List<(Guid Id, TenantStatus Status)> Calls { get; } = [];

        public Task<bool> SetStatusAsync(Guid id, TenantStatus status, DateTimeOffset? trialEndsAt = null, CancellationToken ct = default)
        {
            Calls.Add((id, status));
            return Task.FromResult(true);
        }
    }

    private RecordingStatuses BuildOffboarding()
    {
        var statuses = new RecordingStatuses();
        var storage = Path.Combine(Path.GetTempPath(), "modulus-analytics-offboarding-" + Guid.NewGuid().ToString("N"));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["TenantLifecycle:CertificateSigningKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            ["TenantLifecycle:ExportEncryptionKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
        }).Build();

        Build(extra: s =>
        {
            s.Configure<AnalyticsBackfillOptions>(o =>
            {
                o.PageSize = 1;
                o.PagesPerCycle = 1;
                o.RunsPerCycle = 10;
            });
            s.AddSingleton<ITenantStore>(new FakeTenantStore(_tenantA, _tenantB));
            s.AddSingleton<ITenantStatusWriter>(statuses);
            s.AddSingleton<IFileStorage>(new LocalFileStorage(Options.Create(new StorageOptions { BasePath = storage })));
            s.AddScoped<ICurrentTenant, ScopedTenant>();
            s.AddTenantLifecycle(configuration, lifecycle => lifecycle.AddModulusAnalyticsTenantData());
        });
        return statuses;
    }

    private Task<Guid> RequestOffboarding(Guid tenant)
        => InScope(async sp => (await sp.GetRequiredService<ITenantLifecycleProvider>().RequestOffboardingAsync(tenant, "operator", exportFirst: false)).Id);

    private Task<int> RunOperation(Guid operationId)
        => InScope(async sp =>
        {
            await sp.GetRequiredService<ITenantLifecycleProvider>().RunOperationAsync(operationId);
            return 0;
        });

    private Task<LifecycleOperation> Operation(Guid operationId)
        => InScope(async sp => (await sp.GetRequiredService<ITenantLifecycleProvider>().GetOperationAsync(operationId))!);

    /// <summary>Counts what the analytics handler holds for a company, entered the way the lifecycle provider enters it.</summary>
    private Task<long> HeldFor(Guid tenant, string dataset)
        => InScope(async sp =>
        {
            using var entered = sp.EnterTenant(new TenantInfo(tenant, tenant.ToString("N")));
            return await ActivatorUtilities.CreateInstance<AnalyticsTenantDataHandler>(sp)
                .CountAsync(new TenantDataset(dataset, TenantDataCategory.Owned), CancellationToken.None);
        });

    [Fact]
    public async Task Offboarding_through_the_lifecycle_provider_leaves_no_analytics_copy_even_with_a_run_still_reading()
    {
        var statuses = BuildOffboarding();
        await SeedOrdersAsync(_tenantA, 3);
        await SeedOrdersAsync(_tenantB, 1, firstId: 10);
        await Start([_tenantA, _tenantB]);
        await Cycle(); // company A's run has read one page of three and is still running, with a row queued

        var id = await RequestOffboarding(_tenantA);
        await RunOperation(id);

        var operation = await Operation(id);
        operation.State.Should().Be(OperationState.Completed);
        operation.HasStep("disable:analytics").Should().BeTrue("the analytics step ran between the suspension and the erase");
        statuses.Calls.Should().Contain((_tenantA, TenantStatus.Suspended));
        operation.Outcomes.Where(o => o.Module == "Analytics").Select(o => o.Dataset).Should().Equal(
            AnalyticsTenantDataHandler.BackfillDataset, AnalyticsTenantDataHandler.QueueDataset, AnalyticsTenantDataHandler.WarehouseDataset);

        // Whatever the workers do afterwards, company A's data does not come back, and company B is unaffected.
        await DrainRunsAsync();
        await DrainDeliveriesAsync();
        _sink.Rows.Count(r => r.TenantId == _tenantA).Should().Be(0);
        (await Queued()).Count(q => q.TenantId == _tenantA).Should().Be(0);
        (await Runs()).Count(r => r.TenantId == _tenantA).Should().Be(0);
        _sink.Rows.Count(r => r.TenantId == _tenantB).Should().Be(1);
    }

    [Fact]
    public async Task Offboarding_stops_at_the_analytics_step_while_a_batch_is_in_flight_and_finishes_when_it_is_run_again()
    {
        BuildOffboarding();
        await SeedOrdersAsync(_tenantA, 2);
        await Start([_tenantA]);
        await DrainRunsAsync();
        await HoldClaim(_tenantA, "another-worker", DateTime.UtcNow.AddHours(1)); // a delivery batch being sent right now

        var id = await RequestOffboarding(_tenantA);
        var first = () => RunOperation(id);
        await first.Should().ThrowAsync<InvalidOperationException>();

        var stopped = await Operation(id);
        stopped.State.Should().Be(OperationState.Failed);
        stopped.HasStep("suspend").Should().BeTrue();
        stopped.HasStep("disable:analytics").Should().BeFalse();
        stopped.Outcomes.Should().BeEmpty("nothing is erased while the batch could still land");
        (await HeldFor(_tenantA, AnalyticsTenantDataHandler.QueueDataset)).Should().BeGreaterThan(0);

        await HoldClaim(_tenantA, null, null); // the batch finished
        await RunOperation(id);

        var done = await Operation(id);
        done.State.Should().Be(OperationState.Completed);
        (await HeldFor(_tenantA, AnalyticsTenantDataHandler.QueueDataset)).Should().Be(0);
        (await HeldFor(_tenantA, AnalyticsTenantDataHandler.WarehouseDataset)).Should().Be(0);
    }

    [Fact]
    public async Task A_backfill_for_every_company_skips_a_company_that_was_offboarded()
    {
        BuildOffboarding();
        await SeedOrdersAsync(_tenantA, 2);
        await SeedOrdersAsync(_tenantB, 1, firstId: 10);
        var id = await RequestOffboarding(_tenantA);
        await RunOperation(id);

        var result = await InScope(sp => sp.GetRequiredService<AnalyticsBackfillManager>()
            .StartAsync("orders", 1, null, restart: true, "operator", CancellationToken.None));

        result.Offboarded.Should().Be(1);
        result.Created.Should().Be(1, "company B still gets its run");
        (await Runs()).Should().OnlyContain(r => r.TenantId == _tenantB);
    }

    [Fact]
    public async Task Capture_and_delivery_refuse_a_company_once_it_is_offboarded_but_not_another_company()
    {
        BuildOffboarding();
        await SeedOrdersAsync(_tenantA, 1);
        var consent = () => InScope(sp => sp.GetRequiredService<IAnalyticsConsent>().IsGrantedAsync(_tenantA, "orders", CancellationToken.None).AsTask());

        (await consent()).Should().BeTrue("nothing is wrong with the company yet");

        var id = await RequestOffboarding(_tenantA);
        await RunOperation(id);

        (await consent()).Should().BeFalse("a background job that writes for the company later queues nothing and sends nothing");
        (await InScope(sp => sp.GetRequiredService<IAnalyticsConsent>().IsGrantedAsync(_tenantB, "orders", CancellationToken.None).AsTask()))
            .Should().BeTrue("another company is unaffected");
    }
}
