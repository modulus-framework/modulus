using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Modulus.AuditLogging.Security;
using Modulus.AuditLogging.Security.Retention;
using Modulus.Core.Abstractions;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>A11-1: the scheduled retention host runs the pass, keeps the cluster lock, and never archives a chain twice.</summary>
[Trait("Category", "Unit")]
public sealed class SecurityAuditRetentionServiceTests
{
    private static readonly Guid s_company = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly DateTimeOffset s_now = new(2026, 10, 23, 9, 0, 0, TimeSpan.Zero);

    private sealed class RecordingArchive : ISecurityAuditArchive
    {
        public List<SecurityAuditRecord> Archived { get; } = [];

        public Task ArchiveAsync(Guid chainId, IReadOnlyList<SecurityAuditRecord> records, CancellationToken ct = default)
        {
            Archived.AddRange(records);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    // A lock that is either free (and records each lease it hands out) or held by another replica.
    private sealed class FakeLock(bool free) : IDistributedLock
    {
        public List<string> Acquired { get; } = [];
        public int Released { get; private set; }

        public Task<IAsyncDisposable?> TryAcquireAsync(string key, TimeSpan duration, CancellationToken ct = default)
        {
            if (!free)
                return Task.FromResult<IAsyncDisposable?>(null);
            Acquired.Add(key);
            return Task.FromResult<IAsyncDisposable?>(new Lease(() => Released++));
        }

        private sealed class Lease(Action onRelease) : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                onRelease();
                return ValueTask.CompletedTask;
            }
        }
    }

    private static SecurityAuditEvent Event(DateTimeOffset at, string name) => new()
    {
        Category = SecurityAuditCategories.Identity,
        Action = "signin.success",
        TenantId = s_company,
        Actor = name,
        OccurredAt = at,
        Details = new Dictionary<string, string?>(),
    };

    private static async Task<InMemorySecurityAuditStore> StoreWithOldEntriesAsync()
    {
        var store = new InMemorySecurityAuditStore(TimeProvider.System);
        // Three entries from long ago (expired under the two-year default) and one recent one.
        for (var i = 1; i <= 3; i++)
            await store.AppendAsync(Event(s_now.AddDays(-1000 + i), $"old{i}"));
        await store.AppendAsync(Event(s_now.AddDays(-10), "recent"));
        return store;
    }

    private static SecurityAuditRetentionService Service(ISecurityAuditStore store, ISecurityAuditArchive archive, IServiceProvider services, TimeSpan? interval = null)
    {
        var options = Options.Create(new SecurityAuditRetentionOptions
        {
            Enabled = true,
            Retention = TimeSpan.FromDays(730),
            Interval = interval ?? TimeSpan.FromDays(1),
        });
        var retention = new SecurityAuditRetention(store, archive, new SecurityAuditHolds(), new FixedClock(s_now), options);
        return new SecurityAuditRetentionService(retention, services, options, new FixedClock(s_now), NullLogger<SecurityAuditRetentionService>.Instance);
    }

    [Fact]
    public async Task A_pass_archives_and_purges_the_expired_prefix_under_the_lock()
    {
        var store = await StoreWithOldEntriesAsync();
        var archive = new RecordingArchive();
        var locks = new FakeLock(free: true);
        var services = new ServiceCollection().AddSingleton<IDistributedLock>(locks).BuildServiceProvider();

        var results = await Service(store, archive, services).RunOnceAsync();

        results.Single().Outcome.Should().Be(SecurityAuditRetentionOutcome.Purged);
        archive.Archived.Should().HaveCount(3);
        locks.Acquired.Should().Equal(SecurityAuditRetentionService.LockName);
        locks.Released.Should().Be(1, "the lease is released when the pass ends");
    }

    [Fact]
    public async Task A_pass_with_no_archive_keeps_every_entry()
    {
        var store = await StoreWithOldEntriesAsync();
        var services = new ServiceCollection().BuildServiceProvider();

        var results = await Service(store, new NoSecurityAuditArchive(), services).RunOnceAsync();

        results.Single().Outcome.Should().Be(SecurityAuditRetentionOutcome.RefusedNoArchive);
        var chain = (await store.GetHeadsAsync()).Single().ChainId;
        var kept = new List<SecurityAuditRecord>();
        await foreach (var record in store.ReadAsync(chain))
            kept.Add(record);
        kept.Should().HaveCount(4, "nothing is purged without an archive");
    }

    [Fact]
    public async Task A_replica_that_does_not_get_the_lock_runs_nothing()
    {
        var store = await StoreWithOldEntriesAsync();
        var archive = new RecordingArchive();
        var services = new ServiceCollection().AddSingleton<IDistributedLock>(new FakeLock(free: false)).BuildServiceProvider();

        var results = await Service(store, archive, services).RunOnceAsync();

        results.Should().BeEmpty();
        archive.Archived.Should().BeEmpty("another replica runs the pass, so no chain is archived twice");
    }

    [Fact]
    public async Task The_hosted_schedule_runs_passes_until_it_is_stopped()
    {
        var store = await StoreWithOldEntriesAsync();
        var archive = new RecordingArchive();
        var services = new ServiceCollection().BuildServiceProvider();
        var service = Service(store, archive, services, interval: TimeSpan.FromMilliseconds(50));

        await service.StartAsync(CancellationToken.None);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (archive.Archived.Count == 0 && DateTimeOffset.UtcNow < deadline)
            await Task.Delay(20);
        await service.StopAsync(CancellationToken.None);

        archive.Archived.Should().HaveCount(3, "the scheduled pass archived the expired prefix");
    }

    [Fact]
    public void Retention_is_off_until_an_operator_turns_it_on()
    {
        // Decision D8: no entry is purged by default, whether or not an archive is registered.
        new SecurityAuditRetentionOptions().Enabled.Should().BeFalse();
    }

    [Fact]
    public void The_opt_in_registers_the_schedule_binds_its_settings_and_keeps_the_no_archive_default()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Security:Retention:Interval"] = "02:00:00" })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<ISecurityAuditStore>(new InMemorySecurityAuditStore(TimeProvider.System));
        services.AddLogging();
        services.AddModulusSecurityAuditRetention(configuration);
        using var provider = services.BuildServiceProvider();

        provider.GetServices<IHostedService>().OfType<SecurityAuditRetentionService>().Should().ContainSingle();
        provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<SecurityAuditRetentionOptions>>().Value.Interval
            .Should().Be(TimeSpan.FromHours(2));
        provider.GetRequiredService<ISecurityAuditArchive>().Should().BeOfType<NoSecurityAuditArchive>();
    }
}
