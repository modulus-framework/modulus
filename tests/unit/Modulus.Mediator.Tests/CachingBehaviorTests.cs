using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using Modulus.Mediator.Abstractions.Attributes;
using Modulus.Mediator.Behaviors;
using Xunit;

namespace Modulus.Mediator.Tests;

[Trait("Category", "Unit")]
public sealed class CachingBehaviorTests
{
    private static readonly TenantInfo TenantA = new(Guid.NewGuid(), "tenant-a");
    private static readonly TenantInfo TenantB = new(Guid.NewGuid(), "tenant-b");

    [Fact]
    public async Task SameTenant_SamePayload_SecondCallServedFromCache()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var switchableTenant = new SwitchableTenant { CurrentTenant = TenantA };
        var behavior = new CachingBehavior<CachedQuery, string>(cache, switchableTenant);
        var counter = new Counter();

        await Invoke(behavior, counter, "p1");
        await Invoke(behavior, counter, "p1");

        counter.Calls.Should().Be(1);
    }

    [Fact]
    public async Task DifferentTenants_SamePayload_NeverShareEntries()
    {
        // Regression guard for the cross-tenant leak: under the old flat keys
        // tenant B got whatever tenant A's identical query had just cached.
        var cache = new MemoryCache(new MemoryCacheOptions());
        var switchableTenant = new SwitchableTenant();
        var behavior = new CachingBehavior<CachedQuery, string>(cache, switchableTenant);
        var counter = new Counter();

        switchableTenant.CurrentTenant = TenantA;
        await Invoke(behavior, counter);

        switchableTenant.CurrentTenant = TenantB;
        await Invoke(behavior, counter);

        counter.Calls.Should().Be(2);
    }

    [Fact]
    public async Task HostContext_SharesOnePartition_ButIsolatedFromTenants()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var switchableTenant = new SwitchableTenant();
        var behavior = new CachingBehavior<CachedQuery, string>(cache, switchableTenant);
        var counter = new Counter();

        switchableTenant.CurrentTenant = null;  // host
        await Invoke(behavior, counter);        // populates host partition
        await Invoke(behavior, counter);        // served from cache

        switchableTenant.CurrentTenant = TenantA;
        await Invoke(behavior, counter);        // must NOT see host's cache

        counter.Calls.Should().Be(2);
    }

    [Fact]
    public async Task DifferentPayloads_InSameTenant_CacheSeparately()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var switchableTenant = new SwitchableTenant { CurrentTenant = TenantA };
        var behavior = new CachingBehavior<CachedQuery, string>(cache, switchableTenant);
        var counter = new Counter();

        await Invoke(behavior, counter, "first");
        await Invoke(behavior, counter, "second");

        counter.Calls.Should().Be(2);
    }

    [Fact]
    public async Task UncachedRequest_AlwaysInvokesNext()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var behavior =
            new CachingBehavior<UncachedQuery, string>(cache, null);
        var calls = 0;
        RequestHandlerDelegate<string> next = () =>
        {
            calls++;
            return Task.FromResult("no-attr");
        };

        await behavior.HandleAsync(new UncachedQuery(), next, default);
        await behavior.HandleAsync(new UncachedQuery(), next, default);

        calls.Should().Be(2);
    }

    // ── Helpers & test doubles ──────────────────────────────────────

    private static CachingBehavior<CachedQuery, string> Create(IMemoryCache cache)
        => new(cache);

    private static async Task<string> Invoke(
        CachingBehavior<CachedQuery, string> behavior,
        Counter counter,
        string payload = "test")
    {
        return await behavior.HandleAsync(
            new CachedQuery(payload),
            () =>
            {
                counter.Calls++;
                return Task.FromResult($"result:{payload}");
            },
            default);
    }

    private sealed class Counter
    {
        public int Calls;
    }

    private sealed class SwitchableTenant : ICurrentTenant
    {
        public TenantInfo? CurrentTenant { get; set; }

        public Guid? TenantId => CurrentTenant?.TenantId;
        public string? TenantSlug => CurrentTenant?.TenantSlug;
        public bool IsAvailable => true;
        public bool IsHost => CurrentTenant is null;

        public IDisposable Change(TenantInfo? tenant)
        {
            var previous = CurrentTenant;
            CurrentTenant = tenant;
            return new PopScope(this, previous);
        }

        public IDisposable BeginScope() => Change(CurrentTenant);

        private sealed class PopScope(SwitchableTenant source, TenantInfo? previous) : IDisposable
        {
            public void Dispose() => source.CurrentTenant = previous;
        }
    }

    private sealed class FakeCurrentTenant : ICurrentTenant
    {
        private readonly AsyncLocal<TenantInfo?> _current = new();

        public FakeCurrentTenant(TenantInfo? initial) => _current.Value = initial;

        public Guid? TenantId => _current.Value?.TenantId;
        public string? TenantSlug => _current.Value?.TenantSlug;
        public bool IsAvailable => true;
        public bool IsHost => _current.Value is null;

        public IDisposable Change(TenantInfo? tenant)
        {
            var previous = _current.Value;
            _current.Value = tenant;
            return new PopScope(_current, previous);
        }

        public IDisposable BeginScope()
            => Change(_current.Value);

        private sealed class PopScope(
            AsyncLocal<TenantInfo?> source, TenantInfo? previous) : IDisposable
        {
            public void Dispose() => source.Value = previous;
        }
    }

    [CacheFor(60)]
    public sealed record CachedQuery(string Payload);

    public sealed record UncachedQuery(string? Unused = null);
}
