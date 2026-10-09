namespace Modulus.AI.Connector.Tests;

using FluentAssertions;
using Microsoft.Extensions.Options;
using Modulus.AI.Connector.Contract;
using Modulus.AI.Connector.Revocation;
using Modulus.Core.Abstractions;
using Xunit;

[Trait("Category", "Unit")]
public sealed class DurabilityTests
{
    private static IOptions<ModulusAiConnectorOptions> Spool(string file)
        => Options.Create(new ModulusAiConnectorOptions { Platform = { RevocationSpoolFile = file } });

    [Fact]
    public void Pending_revocation_signals_survive_a_restart_until_acknowledged()
    {
        var file = Path.Combine(Path.GetTempPath(), $"ai-spool-{Guid.NewGuid():N}.json");
        try
        {
            var signal = new RevocationSignal("key-1", "inst-1", "grant.revoked", DateTimeOffset.UtcNow);
            new AiRevocationQueue(Spool(file)).Enqueue(signal);

            var restarted = new AiRevocationQueue(Spool(file));
            restarted.Count.Should().Be(1);
            restarted.TryPeek(out var recovered).Should().BeTrue();
            recovered.RevocationKey.Should().Be("key-1");

            restarted.Acknowledge(recovered);
            File.Exists(file).Should().BeFalse("nothing is pending any more");
            new AiRevocationQueue(Spool(file)).Count.Should().Be(0);
        }
        finally
        {
            File.Delete(file);
        }
    }

    private sealed class SharedLock : IDistributedLock
    {
        public HashSet<string> Taken { get; } = [];

        public Task<IAsyncDisposable?> TryAcquireAsync(string key, TimeSpan duration, CancellationToken ct = default)
            => Task.FromResult<IAsyncDisposable?>(Taken.Add(key) ? new Lease() : null);

        private sealed class Lease : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task A_replayed_envelope_is_refused_even_on_another_node()
    {
        var shared = new SharedLock();
        var expires = DateTimeOffset.UtcNow.AddMinutes(1);
        var nodeA = new AiEnvelopeReplayCache(TimeProvider.System, shared);
        var nodeB = new AiEnvelopeReplayCache(TimeProvider.System, shared);

        (await nodeA.TryRecordAsync("jti-1", expires)).Should().BeTrue();
        (await nodeB.TryRecordAsync("jti-1", expires)).Should().BeFalse("node A already took the cluster-wide lease");
        (await nodeB.TryRecordAsync("jti-2", expires)).Should().BeTrue();
    }
}
