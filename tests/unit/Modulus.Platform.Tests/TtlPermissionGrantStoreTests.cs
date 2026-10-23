using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Modulus.Authorization;
using Modulus.Authorization.Grants;
using Modulus.Core.Abstractions;
using Modulus.MultiTenancy;
using Xunit;

namespace Modulus.Platform.Tests;

[Trait("Category", "Unit")]
public sealed class TtlPermissionGrantStoreTests
{
    private static readonly Guid User = Guid.Parse("00000000-0000-0000-0000-00000000c0de");

    private sealed class ManualClock(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = start;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class CountingStore(IPermissionGrantStore inner) : IPermissionGrantStore
    {
        public int Reads { get; private set; }

        public IReadOnlyCollection<PermissionGrant> GetGrants(PrincipalGrantQuery principal) => inner.GetGrants(principal);

        public Task<IReadOnlyCollection<PermissionGrant>> GetGrantsAsync(PrincipalGrantQuery principal, CancellationToken ct = default)
        {
            Reads++;
            return inner.GetGrantsAsync(principal, ct);
        }
    }

    private sealed class Fixture
    {
        public required CountingStore Inner { get; init; }
        public required ManualClock Clock { get; init; }
        public required TtlPermissionGrantStore Store { get; init; }
        public required ServiceProvider Services { get; init; }
    }

    private static Fixture Create(TimeSpan duration)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentTenant, CurrentTenant>();
        var provider = services.BuildServiceProvider();
        var inner = new CountingStore(new InMemoryPermissionGrantStore().GrantToUser(User, "ops:read"));
        var clock = new ManualClock(new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero));
        var options = Options.Create(new PermissionGrantCacheOptions { Duration = duration });
        return new Fixture
        {
            Inner = inner,
            Clock = clock,
            Store = new TtlPermissionGrantStore(inner, provider.GetRequiredService<IServiceScopeFactory>(), options, clock),
            Services = provider,
        };
    }

    private static PrincipalGrantQuery Query() => new(User, []);

    [Fact]
    public async Task With_the_default_duration_every_read_goes_to_the_store()
    {
        var f = Create(TimeSpan.Zero);

        await f.Store.GetGrantsAsync(Query());
        await f.Store.GetGrantsAsync(Query());

        f.Inner.Reads.Should().Be(2, "the cache is off by default, so a revocation applies on the next check");
    }

    [Fact]
    public async Task Within_the_duration_a_principals_grants_are_served_from_memory()
    {
        var f = Create(TimeSpan.FromSeconds(30));

        var first = await f.Store.GetGrantsAsync(Query());
        var second = await f.Store.GetGrantsAsync(Query());

        f.Inner.Reads.Should().Be(1);
        second.Should().BeEquivalentTo(first);
    }

    [Fact]
    public async Task After_the_duration_the_store_is_read_again()
    {
        var f = Create(TimeSpan.FromSeconds(30));
        await f.Store.GetGrantsAsync(Query());

        f.Clock.Now = f.Clock.Now.AddSeconds(31);
        await f.Store.GetGrantsAsync(Query());

        f.Inner.Reads.Should().Be(2);
    }

    [Fact]
    public async Task An_access_change_empties_the_copy_at_once()
    {
        var f = Create(TimeSpan.FromMinutes(10));
        await f.Store.GetGrantsAsync(Query());

        await f.Store.OnAccessChangedAsync(new AccessChange { Kind = AccessChangeKinds.Grant, Reason = "grant.revoked", UserId = User });
        await f.Store.GetGrantsAsync(Query());

        f.Inner.Reads.Should().Be(2, "a change this node heard about must not wait for the duration to end");
    }

    [Fact]
    public async Task A_company_gets_its_own_copy()
    {
        var f = Create(TimeSpan.FromMinutes(10));
        var tenants = new CurrentTenant();
        using (tenants.Change(new TenantInfo(Guid.NewGuid(), "a")))
            await f.Store.GetGrantsAsync(Query());
        using (tenants.Change(new TenantInfo(Guid.NewGuid(), "b")))
            await f.Store.GetGrantsAsync(Query());

        f.Inner.Reads.Should().Be(2, "the same user can hold different grants in two companies");
    }

    private sealed class GatedStore(IPermissionGrantStore inner, TaskCompletionSource gate) : IPermissionGrantStore
    {
        public int Reads { get; private set; }

        public IReadOnlyCollection<PermissionGrant> GetGrants(PrincipalGrantQuery principal) => inner.GetGrants(principal);

        public async Task<IReadOnlyCollection<PermissionGrant>> GetGrantsAsync(PrincipalGrantQuery principal, CancellationToken ct = default)
        {
            Reads++;
            await gate.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
            return await inner.GetGrantsAsync(principal, ct);
        }
    }

    [Fact]
    public async Task Concurrent_misses_for_one_principal_share_a_single_store_read()
    {
        var gate = new TaskCompletionSource();
        var inner = new GatedStore(new InMemoryPermissionGrantStore().GrantToUser(User, "ops:read"), gate);
        var services = new ServiceCollection().AddSingleton<ICurrentTenant, CurrentTenant>().BuildServiceProvider();
        var store = new TtlPermissionGrantStore(inner, services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new PermissionGrantCacheOptions { Duration = TimeSpan.FromMinutes(1) }), new ManualClock(DateTimeOffset.UtcNow));

        var reads = Enumerable.Range(0, 8).Select(_ => store.GetGrantsAsync(Query())).ToArray();
        await Task.Yield();
        gate.SetResult();
        var results = await Task.WhenAll(reads);

        inner.Reads.Should().Be(1, "eight concurrent checks for one principal wait for the same read");
        results.Should().AllSatisfy(r => r.Should().ContainSingle(g => g.Permission == "ops:read"));
    }

    [Fact]
    public async Task A_revoked_grant_stops_applying_after_the_change_is_announced()
    {
        var registry = new PermissionRegistry();
        registry.Add("ops:read", "read");
        registry.Freeze();
        var inMemory = new InMemoryPermissionGrantStore().GrantToUser(User, "ops:read");
        var clock = new ManualClock(DateTimeOffset.UtcNow);
        var services = new ServiceCollection().AddSingleton<ICurrentTenant, CurrentTenant>().BuildServiceProvider();
        var store = new TtlPermissionGrantStore(inMemory, services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new PermissionGrantCacheOptions { Duration = TimeSpan.FromMinutes(10) }), clock);
        var resolver = new PermissionResolver(store, registry);
        // The authorization handler reads grants through the async path, which is the one the copy serves.
        (await store.GetGrantsAsync(Query())).Should().NotBeEmpty();
        resolver.Resolve(Query(), await store.GetGrantsAsync(Query())).Should().Contain("ops:read");

        inMemory.RevokeFromUser(User, "ops:read");
        await store.OnAccessChangedAsync(new AccessChange { Kind = AccessChangeKinds.Grant, Reason = "grant.revoked", UserId = User });

        resolver.Resolve(Query(), await store.GetGrantsAsync(Query())).Should().NotContain("ops:read");
    }
}
