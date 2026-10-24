using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions.Exceptions;
using Modulus.Core.Abstractions.Secrets;
using Modulus.Secrets;
using Xunit;

namespace Modulus.Platform.Tests;

[Trait("Category", "Unit")]
public sealed class SecretsTests
{
    private static readonly SecretReference Primary = SecretReference.Parse("tenant-db/3f2c0000/primary");

    [Theory]
    [InlineData("tenant-db/3f2c0000/primary")]
    [InlineData("a/b")]
    [InlineData("a/b/c/d/e/f/g")]
    public void Reference_AcceptsCanonicalPaths(string path)
    {
        SecretReference.TryParse(path, out var reference).Should().BeTrue();
        reference!.Path.Should().Be(path);
    }

    [Theory]
    [InlineData("")]
    [InlineData("single")]
    [InlineData("a/../b")]
    [InlineData("/leading/slash")]
    [InlineData("trailing/")]
    [InlineData("a//b")]
    [InlineData("Upper/case")]
    [InlineData("tenant-db/data/x")]
    [InlineData("tenant-db/metadata/x")]
    [InlineData("a/b/c/d/e/f/g/h")]
    [InlineData("has.dot/x")]
    public void Reference_RejectsPathsThatCouldEscapeTheNamespace(string path)
        => SecretReference.TryParse(path, out _).Should().BeFalse();

    [Fact]
    public void SecretValue_And_SensitiveString_NeverPrintTheirContent()
    {
        var value = new SecretValue(new Dictionary<string, string> { ["connectionString"] = "Password=hunter2" }, 3);
        var sensitive = new SensitiveString("Password=hunter2");

        value.ToString().Should().Be("***");
        sensitive.ToString().Should().Be("***");
        sensitive.Reveal().Should().Be("Password=hunter2");
    }

    [Fact]
    public async Task Cache_ServesFreshEntry_WithoutAskingTheBackendAgain()
    {
        var backend = Sync(_ => Value(1));
        var store = Store(backend, new FakeClock());

        await store.GetAsync(Primary);
        await store.GetAsync(Primary);

        backend.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Cache_RefetchesAfterTtl()
    {
        var backend = Sync(call => Value(call));
        var clock = new FakeClock();
        var store = Store(backend, clock);

        await store.GetAsync(Primary);
        clock.Advance(TimeSpan.FromMinutes(6));
        var second = await store.GetAsync(Primary);

        second.Version.Should().Be(2);
        backend.Calls.Should().Be(2);
    }

    [Fact]
    public async Task Cache_ServesStaleValue_WhileBackendIsDown_WithinTheBound()
    {
        var backend = Sync(call => call == 1 ? Value(1) : throw Down());
        var clock = new FakeClock();
        var store = StaleStore(backend, clock);

        await store.GetAsync(Primary);
        clock.Advance(TimeSpan.FromMinutes(10));
        var stale = await store.GetAsync(Primary);

        stale.Version.Should().Be(1);
    }

    [Fact]
    public async Task Cache_FailsClosed_OnceTheStaleBoundIsPassed()
    {
        var backend = Sync(call => call == 1 ? Value(1) : throw Down());
        var clock = new FakeClock();
        var store = StaleStore(backend, clock);

        await store.GetAsync(Primary);
        clock.Advance(TimeSpan.FromMinutes(16));

        var act = () => store.GetAsync(Primary);
        await act.Should().ThrowAsync<SecretStoreUnavailableException>();
    }

    [Fact]
    public async Task Cache_DoesNotCacheMissingSecrets()
    {
        var backend = Sync(call => call == 1 ? throw new SecretNotFoundException("missing") : Value(1));
        var store = Store(backend, new FakeClock());

        await Assert.ThrowsAsync<SecretNotFoundException>(() => store.GetAsync(Primary));
        var found = await store.GetAsync(Primary);

        found.Version.Should().Be(1);
        backend.Calls.Should().Be(2);
    }

    [Fact]
    public async Task Cache_SharesOneBackendCall_AmongConcurrentMisses()
    {
        var backend = Async(async _ =>
        {
            await Task.Delay(50);
            return Value(1);
        });
        var store = Store(backend, new FakeClock());

        var callers = Enumerable.Range(0, 20).Select(_ => store.GetAsync(Primary)).ToArray();
        await Task.WhenAll(callers);

        backend.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Evict_ForcesTheNextReadToTheBackend()
    {
        var backend = Sync(call => Value(call));
        var store = Store(backend, new FakeClock());

        await store.GetAsync(Primary);
        ((ISecretCache)store).Evict(Primary);
        var after = await store.GetAsync(Primary);

        after.Version.Should().Be(2);
    }

    [Fact]
    public async Task InMemoryWriter_EnforcesCheckAndSet()
    {
        var store = new InMemorySecretStore();
        var data = new Dictionary<string, string> { ["connectionString"] = "x" };

        var v1 = await store.WriteAsync(Primary, data, expectedVersion: null);
        var v2 = await store.WriteAsync(Primary, data, expectedVersion: v1);
        var act = () => store.WriteAsync(Primary, data, expectedVersion: v1);

        v2.Should().Be(2);
        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task ConfigurationBackend_ReadsSecretsSection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Secrets:Values:tenant-db/3f2c0000/primary:connectionString"] = "Server=db",
            })
            .Build();

        var value = await new ConfigurationSecretStore(configuration).GetAsync(Primary);

        value.Get("connectionString").Should().Be("Server=db");
    }

    [Fact]
    public void The_default_maximum_age_of_a_cached_secret_is_five_minutes()
    {
        // Decision 21: a cached secret is never served past five minutes, fresh or while the store is unreachable.
        var options = new SecretsOptions();

        options.Ttl.Should().Be(TimeSpan.FromMinutes(5));
        options.MaxStaleOnFailure.Should().Be(TimeSpan.FromMinutes(5));
        options.Validate().Should().BeNull();
    }

    [Fact]
    public void Options_RejectStaleBoundShorterThanTtl()
    {
        var options = new SecretsOptions { Ttl = TimeSpan.FromMinutes(10), MaxStaleOnFailure = TimeSpan.FromMinutes(5) };

        options.Validate().Should().NotBeNull();
    }

    private static SecretValue Value(int version) =>
        new(new Dictionary<string, string> { ["connectionString"] = $"v{version}" }, version);

    private static SecretStoreUnavailableException Down() => new("down");

    private static CachedSecretStore StaleStore(ISecretBackend backend, TimeProvider clock) =>
        new(backend, Options.Create(new SecretsOptions { Ttl = TimeSpan.FromMinutes(1), MaxStaleOnFailure = TimeSpan.FromMinutes(15) }), clock, NullLogger<CachedSecretStore>.Instance);

    private static CachedSecretStore Store(ISecretBackend backend, TimeProvider clock) =>
        new(backend, Options.Create(new SecretsOptions()), clock, NullLogger<CachedSecretStore>.Instance);

    /// <summary>Answers each call from a script; the script receives the 1-based call number.</summary>
    private sealed class ScriptedBackend(Func<int, Task<SecretValue>> script) : ISecretBackend
    {
        private int _calls;

        public int Calls => _calls;

        public Task<SecretValue> GetAsync(SecretReference reference, CancellationToken cancellationToken = default)
            => script(Interlocked.Increment(ref _calls));
    }

    private static ScriptedBackend Sync(Func<int, SecretValue> script) => new(call => Task.FromResult(script(call)));

    private static ScriptedBackend Async(Func<int, Task<SecretValue>> script) => new(script);

    private sealed class FakeClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public FakeClock() : this(DateTimeOffset.Parse("2026-10-18T10:00:00Z")) { }

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }
}
