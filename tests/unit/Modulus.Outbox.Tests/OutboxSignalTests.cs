using FluentAssertions;
using Modulus.Outbox;
using Xunit;

namespace Modulus.Outbox.Tests;

[Trait("Category", "Unit")]
public sealed class OutboxSignalTests
{
    [Fact]
    public async Task Notify_wakes_a_waiter_well_before_the_interval()
    {
        var signal = new OutboxSignal();
        var wait = signal.WaitAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
        signal.Notify();

        var finished = await Task.WhenAny(wait, Task.Delay(TimeSpan.FromSeconds(5)));
        finished.Should().BeSameAs(wait);
    }

    [Fact]
    public async Task Notifications_before_the_wait_are_not_lost_and_collapse_into_one()
    {
        var signal = new OutboxSignal();
        signal.Notify();
        signal.Notify();

        await signal.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);

        var started = DateTime.UtcNow;
        await signal.WaitAsync(TimeSpan.FromMilliseconds(100), CancellationToken.None);
        (DateTime.UtcNow - started).Should().BeGreaterThan(TimeSpan.FromMilliseconds(50));
    }

    [Fact]
    public async Task Wait_returns_after_the_interval_without_a_notification()
    {
        var signal = new OutboxSignal();
        await signal.WaitAsync(TimeSpan.FromMilliseconds(50), CancellationToken.None);
    }

    [Fact]
    public async Task Wait_honours_cancellation()
    {
        var signal = new OutboxSignal();
        using var cts = new CancellationTokenSource(50);
        var act = () => signal.WaitAsync(TimeSpan.FromSeconds(30), cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
