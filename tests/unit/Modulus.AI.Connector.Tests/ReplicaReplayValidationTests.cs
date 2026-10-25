using FluentAssertions;
using Modulus.AI.Connector;
using Modulus.Core.Abstractions;
using Xunit;

namespace Modulus.AI.Connector.Tests;

/// <summary>Several replicas need a shared lock for envelope replay protection; without one the app refuses to start.</summary>
[Trait("Category", "Unit")]
public sealed class ReplicaReplayValidationTests
{
    [Fact]
    public void Several_replicas_without_a_distributed_lock_fail_validation()
    {
        var options = new ModulusAiConnectorOptions { Enabled = true };
        options.Platform.ReplicaCount = 2;

        var result = new AiConnectorOptionsValidator(distributedLock: null).Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("ReplicaCount");
    }

    [Fact]
    public void Several_replicas_with_a_distributed_lock_pass_this_check()
    {
        var options = new ModulusAiConnectorOptions { Enabled = true };
        options.Platform.ReplicaCount = 2;

        var result = new AiConnectorOptionsValidator(new NoLock()).Validate(null, options);

        result.FailureMessage?.Should().NotContain("ReplicaCount");
    }

    [Fact]
    public void One_replica_needs_no_lock()
    {
        var options = new ModulusAiConnectorOptions { Enabled = true };

        var result = new AiConnectorOptionsValidator(distributedLock: null).Validate(null, options);

        result.FailureMessage?.Should().NotContain("ReplicaCount");
    }

    [Fact]
    public void The_default_envelope_lifetime_cap_is_two_minutes()
    {
        new ModulusAiConnectorOptions().Platform.MaxEnvelopeLifetime.Should().Be(TimeSpan.FromMinutes(2));
    }

    private sealed class NoLock : IDistributedLock
    {
        public Task<IAsyncDisposable?> TryAcquireAsync(string key, TimeSpan duration, CancellationToken ct = default)
            => Task.FromResult<IAsyncDisposable?>(null);
    }
}
