using FluentAssertions;
using Microsoft.Extensions.Options;
using Modulus.AspNetCore.Idempotency;
using Xunit;

namespace Modulus.AspNetCore.Tests;

[Trait("Category", "Unit")]
public sealed class InMemoryIdempotencyStoreTests
{
    private static readonly CachedResponse SampleResponse =
        new(200, new Dictionary<string, string> { ["Content-Type"] = "application/json" }, [1, 2, 3]);

    private static InMemoryIdempotencyStore NewStore(int retentionSeconds = 3600, TimeProvider? clock = null)
        => new(Options.Create(new IdempotencyOptions { RetentionSeconds = retentionSeconds }), clock);

    [Fact]
    public async Task FirstClaim_Starts()
    {
        var store = NewStore();

        var result = await store.TryBeginAsync("k", "fp", default);

        result.Status.Should().Be(IdempotencyStatus.Started);
    }

    [Fact]
    public async Task SecondClaim_BeforeCompletion_IsInProgress()
    {
        var store = NewStore();
        await store.TryBeginAsync("k", "fp", default);

        var result = await store.TryBeginAsync("k", "fp", default);

        result.Status.Should().Be(IdempotencyStatus.InProgress);
        result.Fingerprint.Should().Be("fp");
    }

    [Fact]
    public async Task ClaimAfterCompletion_ReplaysResponse()
    {
        var store = NewStore();
        await store.TryBeginAsync("k", "fp", default);
        await store.CompleteAsync("k", SampleResponse, default);

        var result = await store.TryBeginAsync("k", "fp", default);

        result.Status.Should().Be(IdempotencyStatus.Completed);
        result.Response.Should().BeSameAs(SampleResponse);
        result.Fingerprint.Should().Be("fp");
    }

    [Fact]
    public async Task Abandon_ReleasesClaim_SoRetryStartsFresh()
    {
        var store = NewStore();
        await store.TryBeginAsync("k", "fp", default);
        await store.AbandonAsync("k", default);

        var result = await store.TryBeginAsync("k", "fp", default);

        result.Status.Should().Be(IdempotencyStatus.Started);
    }

    [Fact]
    public async Task ExpiredClaim_IsEvicted_AndReclaimable()
    {
        var clock = new FakeClock(DateTimeOffset.UnixEpoch);
        var store = NewStore(retentionSeconds: 60, clock: clock);
        await store.TryBeginAsync("k", "fp", default);
        await store.CompleteAsync("k", SampleResponse, default);

        clock.Advance(TimeSpan.FromSeconds(61));

        var result = await store.TryBeginAsync("k", "fp", default);

        result.Status.Should().Be(IdempotencyStatus.Started); // stale entry evicted, not replayed
    }

    private sealed class FakeClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }
}
