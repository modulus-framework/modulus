using FluentAssertions;
using Microsoft.AspNetCore.Http;
using System.Threading.RateLimiting;
using Modulus.AspNetCore.RateLimiting;
using Xunit;

namespace Modulus.AspNetCore.Tests;

[Trait("Category", "Unit")]
public sealed class EvictableFixedWindowLimiterTests
{
    private const int SweepMs = 30;
    private static readonly TimeSpan Window = TimeSpan.FromMilliseconds(20);
    private static readonly TimeSpan Idle = TimeSpan.FromMilliseconds(100);

    // The limiter partitions on HttpContext; tests map string keys onto
    // pre-built contexts so partition identity stays controllable.
    private sealed class TestHost : IDisposable
    {
        private readonly Dictionary<HttpContext, string> _keys =
            new(ReferenceEqualityComparer.Instance);

        public EvictableFixedWindowLimiter Limiter { get; }

        public TestHost()
        {
            Limiter = new EvictableFixedWindowLimiter(
                KeyOf,
                () => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 10,
                    Window = Window,
                    QueueLimit = 0,
                },
                idleThreshold: Idle,
                sweepInterval: TimeSpan.FromMilliseconds(SweepMs));
        }

        public HttpContext ContextFor(string key)
        {
            var ctx = new DefaultHttpContext();
            _keys[ctx] = key;
            return ctx;
        }

        public string KeyOf(HttpContext ctx)
            => _keys.TryGetValue(ctx, out var k) ? k
               : throw new InvalidOperationException("unknown context");

        public void Dispose() => Limiter.Dispose();
    }

    [Fact]
    public async Task Acquisition_CreatesPartition_AndServesPermits()
    {
        using var host = new TestHost();
        var ctxA = host.ContextFor("a");

        var lease = await host.Limiter.AcquireAsync(ctxA);

        lease.IsAcquired.Should().BeTrue();
        host.Limiter.GetStatistics(ctxA).Should().NotBeNull();
        host.Limiter.GetStatistics(host.ContextFor("missing")).Should().BeNull();
    }

    [Fact]
    public async Task EvictIdlePartitions_RemovesOnlyIdlePartitions()
    {
        using var host = new TestHost();
        var idleCtx = host.ContextFor("idle-one");
        await host.Limiter.AcquireAsync(idleCtx);

        // Sleep past the idle threshold so "idle-one" becomes evictable.
        await Task.Delay(Idle + TimeSpan.FromMilliseconds(120));

        // A second partition acquired NOW stays fresh while the first ages on.
        var freshCtx = host.ContextFor("fresh");
        await host.Limiter.AcquireAsync(freshCtx);

        host.Limiter.EvictIdlePartitions().Should().BeGreaterThanOrEqualTo(1);
        host.Limiter.GetStatistics(freshCtx).Should().NotBeNull();
    }

    [Fact]
    public async Task RecentPartition_IsNotEvicted()
    {
        using var host = new TestHost();
        var recentCtx = host.ContextFor("recent");
        await host.Limiter.AcquireAsync(recentCtx);

        // Well below the idle threshold — nothing to evict.
        host.Limiter.EvictIdlePartitions().Should().Be(0);
        host.Limiter.GetStatistics(recentCtx).Should().NotBeNull();
    }

    [Fact]
    public async Task EvictedKey_ReacquiresCleanly_NoDisposeRace()
    {
        // Regression guard for the no-Dispose eviction policy: an evicted
        // partition must never surface ObjectDisposedException to a caller
        // that touches the same key again. Dropping the reference (rather
        // than disposing) means re-acquisition just builds a fresh window.
        using var host = new TestHost();

        await host.Limiter.AcquireAsync(host.ContextFor("reused"));
        await Task.Delay(Idle + TimeSpan.FromMilliseconds(120));
        host.Limiter.EvictIdlePartitions().Should().BeGreaterThanOrEqualTo(1);

        var act = async () =>
        {
            var lease = await host.Limiter.AcquireAsync(host.ContextFor("reused"));
            lease.IsAcquired.Should().BeTrue();
        };

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Dispose_AfterAcquisition_DoesNotThrow()
    {
        var host = new TestHost();
        await host.Limiter.AcquireAsync(host.ContextFor("held"));

        var act = () => host.Dispose();

        act.Should().NotThrow();
    }
}
