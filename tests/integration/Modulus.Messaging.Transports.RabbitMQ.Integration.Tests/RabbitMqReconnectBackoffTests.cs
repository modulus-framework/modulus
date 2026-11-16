using FluentAssertions;
using Xunit;

namespace Modulus.Messaging.Transports.RabbitMQ.Integration.Tests;

/// <summary>The reconnect wait (NFR-02): 1 s, doubling, capped at 60 s. Internal helper, so it sits beside the transport tests.</summary>
[Trait("Category", "Unit")]
public sealed class RabbitMqReconnectBackoffTests
{
    private static readonly TimeSpan Min = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan Max = TimeSpan.FromSeconds(60);

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(6, 32)]
    [InlineData(7, 60)]
    [InlineData(50, 60)]
    public void NFR_02_the_wait_doubles_from_one_second_and_stops_at_sixty(int attempt, int expectedSeconds)
    {
        RabbitMqReconnectBackoff.DelayFor(attempt, Min, Max).Should().Be(TimeSpan.FromSeconds(expectedSeconds));
    }

    [Fact]
    public void NFR_02_a_huge_attempt_count_stays_at_the_maximum_without_overflow()
    {
        RabbitMqReconnectBackoff.DelayFor(int.MaxValue, Min, Max).Should().Be(Max);
    }

    [Fact]
    public void NFR_02_the_options_reject_a_maximum_shorter_than_the_minimum()
    {
        var options = new RabbitMqTransportOptions { ReconnectMinDelay = TimeSpan.FromSeconds(5), ReconnectMaxDelay = TimeSpan.FromSeconds(2) };
        var act = () => new RabbitMqTransport(options);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
