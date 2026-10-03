using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Modulus.BackgroundJobs;
using Modulus.Core.Abstractions;
using Modulus.MultiTenancy;
using Modulus.MultiTenancy.Extensions;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>
/// Jobs and messages used to restore their tenant as <c>new TenantInfo(id, "")</c> with no check,
/// so a forged or stale id became a data scope. <see cref="VerifiedTenantContextRestorer"/> resolves
/// the id through the tenant store and rejects unknown or inactive tenants.
/// </summary>
[Trait("Category", "Unit")]
public sealed class VerifiedTenantContextRestorerTests
{
    private static readonly TenantInfo Acme = new(Guid.NewGuid(), "acme", GroupId: Guid.NewGuid());

    [Fact]
    public async Task A_known_tenant_is_restored_with_its_full_metadata()
    {
        var restorer = new VerifiedTenantContextRestorer(new Store(Acme));

        (await restorer.VerifyAsync(Acme.TenantId)).Should().Be(Acme);
    }

    [Fact]
    public async Task Entering_a_verified_tenant_makes_it_ambient_in_the_callers_frame()
    {
        var current = new CurrentTenant();
        var sp = new ServiceCollection()
            .AddSingleton<ICurrentTenant>(current)
            .AddSingleton<ITenantContextRestorer>(new VerifiedTenantContextRestorer(new Store(Acme)))
            .BuildServiceProvider();

        using (sp.EnterTenant(await sp.VerifyTenantAsync(Acme.TenantId)))
        {
            current.TenantId.Should().Be(Acme.TenantId);
            current.Tenant.Should().Be(Acme);
        }

        current.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task An_unknown_or_inactive_tenant_is_rejected()
    {
        var restorer = new VerifiedTenantContextRestorer(new Store(Acme));

        var act = async () => await restorer.VerifyAsync(Guid.NewGuid());

        (await act.Should().ThrowAsync<TenantContextRejectedException>()).Which.TenantId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Lookups_are_cached_for_the_cache_window_then_rechecked()
    {
        var store = new Store(Acme);
        var clock = new ManualClock();
        var restorer = new VerifiedTenantContextRestorer(store, clock);

        await restorer.VerifyAsync(Acme.TenantId);
        await restorer.VerifyAsync(Acme.TenantId);
        store.Lookups.Should().Be(1);

        store.Deactivate();
        clock.Advance(VerifiedTenantContextRestorer.CacheDuration);

        var act = async () => await restorer.VerifyAsync(Acme.TenantId);
        await act.Should().ThrowAsync<TenantContextRejectedException>();
    }

    [Fact]
    public void AddMultiTenancy_registers_the_verified_restorer()
    {
        var services = new ServiceCollection();
        services.AddMultiTenancy();

        services.BuildServiceProvider().GetRequiredService<ITenantContextRestorer>()
            .Should().BeOfType<VerifiedTenantContextRestorer>();
    }

    [Fact]
    public async Task Without_a_restorer_the_tenant_is_applied_unverified()
    {
        var current = new CurrentTenant();
        var sp = new ServiceCollection().AddSingleton<ICurrentTenant>(current).BuildServiceProvider();
        var id = Guid.NewGuid();

        using (sp.EnterTenant(await sp.VerifyTenantAsync(id)))
            current.TenantId.Should().Be(id);

        current.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task A_job_queued_for_a_rejected_tenant_does_not_run()
    {
        var ran = new ConcurrentBag<Guid?>();
        var services = new ServiceCollection();
        services.AddMultiTenancy();
        services.AddSingleton<ITenantStore>(new Store(Acme));
        services.AddSingleton(ran);
        services.AddTransient<IBackgroundJob<JobArgs>, RecordingJob>();
        var sp = services.BuildServiceProvider();
        var current = sp.GetRequiredService<ICurrentTenant>();
        var queue = new ChannelJobQueue(sp, NullLogger<ChannelJobQueue>.Instance);
        await queue.StartAsync(CancellationToken.None);

        using (current.Change(new TenantInfo(Guid.NewGuid(), "forged")))
            await queue.EnqueueAsync<RecordingJob, JobArgs>(new JobArgs(), CancellationToken.None);
        using (current.Change(new TenantInfo(Acme.TenantId, string.Empty)))
            await queue.EnqueueAsync<RecordingJob, JobArgs>(new JobArgs(), CancellationToken.None);
        await queue.StopAsync(CancellationToken.None);

        ran.Should().ContainSingle().Which.Should().Be(Acme.TenantId);
    }

    private sealed record JobArgs;

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;
    }

    private sealed class RecordingJob(ConcurrentBag<Guid?> ran, ICurrentTenant tenant) : IBackgroundJob<JobArgs>
    {
        public Task ExecuteAsync(JobArgs args, CancellationToken ct)
        {
            ran.Add(tenant.TenantId);
            return Task.CompletedTask;
        }
    }

    private sealed class Store(TenantInfo tenant) : ITenantStore
    {
        private bool _active = true;

        public int Lookups { get; private set; }

        public void Deactivate() => _active = false;

        public Task<TenantInfo?> FindByIdAsync(Guid id, CancellationToken ct)
        {
            Lookups++;
            return Task.FromResult(_active && id == tenant.TenantId ? tenant : null);
        }

        public Task<TenantInfo?> FindBySlugAsync(string slug, CancellationToken ct)
            => Task.FromResult(_active && slug == tenant.TenantSlug ? tenant : null);
    }
}
