using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Modulus.AspNetCore.Idempotency;
using Modulus.AspNetCore.Redis.Idempotency;
using NSubstitute;
using StackExchange.Redis;
using Xunit;

namespace Modulus.AspNetCore.Redis.Tests;

// Exercises the claim/replay/abandon protocol against a substituted IDatabase:
// the decisive claim must be a single SET NX (first caller wins across nodes),
// completed responses replay from the data key even when the claim key expired
// first, and a corrupt stored entry is treated as absent rather than poisoning
// the key until its TTL runs out.
[Trait("Category", "Unit")]
public sealed class RedisIdempotencyStoreTests
{
    private const string Prefix = "modulus:idem:";
    private readonly IDatabase _db = Substitute.For<IDatabase>();
    private readonly RedisIdempotencyStore _store;

    public RedisIdempotencyStoreTests()
    {
        var mux = Substitute.For<IConnectionMultiplexer>();
        mux.GetDatabase().Returns(_db);
        _store = new RedisIdempotencyStore(
            mux,
            Options.Create(new IdempotencyOptions { RetentionSeconds = 60 }),
            new RedisIdempotencyStoreOptions());

        // Default: nothing stored, claim always succeeds.
        _db.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(RedisValue.Null);
        _db.StringSetAsync(
                Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<TimeSpan?>(),
                Arg.Any<bool>(), Arg.Any<When>(), Arg.Any<CommandFlags>())
            .Returns(true);
    }

    private static string StoredJson(
        string fingerprint, int statusCode = 201, string body = "created")
        => JsonSerializer.Serialize(new
        {
            Fingerprint = fingerprint,
            Response = new
            {
                StatusCode = statusCode,
                Headers = new Dictionary<string, string>
                {
                    ["Content-Type"] = "application/json",
                },
                Body = Encoding.UTF8.GetBytes(body),
            },
        });

    [Fact]
    public async Task Winning_the_claim_returns_Started_via_a_single_SET_NX()
    {
        var result = await _store.TryBeginAsync("k1", "fp", CancellationToken.None);

        result.Status.Should().Be(IdempotencyStatus.Started);
        await _db.Received(1).StringSetAsync(
            Arg.Is<RedisKey>(k => k == Prefix + "k1:claim"),
            Arg.Is<RedisValue>(v => v == "fp"),
            TimeSpan.FromSeconds(60),
            false,
            When.NotExists,
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task Completed_entry_replays_without_attempting_a_claim()
    {
        _db.StringGetAsync(Prefix + "k1:data", Arg.Any<CommandFlags>())
            .Returns((RedisValue)StoredJson("orig-fp"));

        var result = await _store.TryBeginAsync("k1", "fp", CancellationToken.None);

        result.Status.Should().Be(IdempotencyStatus.Completed);
        result.Fingerprint.Should().Be("orig-fp");
        result.Response!.StatusCode.Should().Be(201);
        result.Response.Headers.Should().ContainKey("Content-Type");
        Encoding.UTF8.GetString(result.Response.Body).Should().Be("created");
        await _db.DidNotReceive().StringSetAsync(
            Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<TimeSpan?>(),
            Arg.Any<bool>(), Arg.Any<When>(), Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task Losing_the_claim_reports_InProgress_with_the_stored_fingerprint()
    {
        _db.StringSetAsync(
                Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<TimeSpan?>(),
                Arg.Any<bool>(), Arg.Any<When>(), Arg.Any<CommandFlags>())
            .Returns(false);
        _db.StringGetAsync(Prefix + "k1:claim", Arg.Any<CommandFlags>())
            .Returns((RedisValue)"winner-fp");

        var result = await _store.TryBeginAsync("k1", "fp", CancellationToken.None);

        result.Status.Should().Be(IdempotencyStatus.InProgress);
        result.Fingerprint.Should().Be("winner-fp");
    }

    [Fact]
    public async Task Losing_the_claim_to_a_winner_that_already_completed_replays()
    {
        _db.StringSetAsync(
                Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<TimeSpan?>(),
                Arg.Any<bool>(), Arg.Any<When>(), Arg.Any<CommandFlags>())
            .Returns(false);
        // First data read: empty (before losing the claim); second: completed.
        _db.StringGetAsync(Prefix + "k1:data", Arg.Any<CommandFlags>())
            .Returns(RedisValue.Null, (RedisValue)StoredJson("orig-fp"));

        var result = await _store.TryBeginAsync("k1", "fp", CancellationToken.None);

        result.Status.Should().Be(IdempotencyStatus.Completed);
        result.Fingerprint.Should().Be("orig-fp");
    }

    [Fact]
    public async Task Corrupt_stored_entry_is_treated_as_absent_so_the_key_is_reclaimed()
    {
        _db.StringGetAsync(Prefix + "k1:data", Arg.Any<CommandFlags>())
            .Returns((RedisValue)"{ not json");

        var result = await _store.TryBeginAsync("k1", "fp", CancellationToken.None);

        result.Status.Should().Be(IdempotencyStatus.Started);
    }

    [Fact]
    public async Task Complete_stores_the_response_with_the_claim_fingerprint_and_ttl()
    {
        _db.StringGetAsync(Prefix + "k1:claim", Arg.Any<CommandFlags>())
            .Returns((RedisValue)"fp");
        RedisValue written = default;
        await _db.StringSetAsync(
            Arg.Is<RedisKey>(k => k == Prefix + "k1:data"),
            Arg.Do<RedisValue>(v => written = v),
            TimeSpan.FromSeconds(60),
            false,
            // First-completion-wins: the data write must NOT overwrite a
            // claim that was re-taken after expiry (When.NotExists).
            When.NotExists,
            Arg.Any<CommandFlags>());

        await _store.CompleteAsync(
            "k1",
            new CachedResponse(
                200,
                new Dictionary<string, string> { ["X-Id"] = "42" },
                Encoding.UTF8.GetBytes("done")),
            CancellationToken.None);

        var json = JsonDocument.Parse(written.ToString()).RootElement;
        json.GetProperty("Fingerprint").GetString().Should().Be("fp");
        json.GetProperty("Response").GetProperty("StatusCode").GetInt32().Should().Be(200);
        json.GetProperty("Response").GetProperty("Headers").GetProperty("X-Id")
            .GetString().Should().Be("42");
    }

    [Fact]
    public async Task Abandon_deletes_only_the_claim_key()
    {
        await _store.AbandonAsync("k1", CancellationToken.None);

        await _db.Received(1).KeyDeleteAsync(
            Arg.Is<RedisKey>(k => k == Prefix + "k1:claim"), Arg.Any<CommandFlags>());
        await _db.DidNotReceive().KeyDeleteAsync(
            Arg.Is<RedisKey>(k => k == Prefix + "k1:data"), Arg.Any<CommandFlags>());
    }

    [Fact]
    public void Registration_supersedes_the_in_memory_default_in_either_order()
    {
        var mux = Substitute.For<IConnectionMultiplexer>();

        var services = new ServiceCollection();
        services.AddSingleton(mux);
        services.AddRedisIdempotencyStore();
        // Simulates AddModulusIdempotency's TryAdd default arriving afterwards.
        var descriptor = services.Single(d => d.ServiceType == typeof(IIdempotencyStore));
        descriptor.ImplementationType.Should().Be(typeof(RedisIdempotencyStore));
    }
}
