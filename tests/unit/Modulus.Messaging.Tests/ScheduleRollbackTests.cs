using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Modulus.Messaging.Tests;

[Trait("Category", "Unit")]
public sealed class ScheduleRollbackTests
{
    [Fact]
    public async Task A_failed_schedule_reports_its_own_error_even_when_the_transport_cannot_cancel()
    {
        var services = new ServiceCollection();
        services.AddModulusMessaging(b => b.UseTransport(_ => new FailingScheduleTransport()));
        await using var provider = services.BuildServiceProvider();
        var scheduler = provider.GetRequiredService<IMessageScheduler>();

        var act = () => scheduler.ScheduleSend(new Uri("queue:anything"), DateTimeOffset.UtcNow.AddMinutes(1), new ScheduleRollbackMessage());

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("the write failed");
    }

    private sealed record ScheduleRollbackMessage;

    private sealed class FailingScheduleTransport : IMessageTransport, IScheduledMessageTransport
    {
        public Task Start(IReadOnlyCollection<string> endpoints, Func<MessageEnvelope, CancellationToken, Task> receive, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task Stop(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task Send(MessageEnvelope envelope, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task Redeliver(MessageEnvelope envelope, TimeSpan delay, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task MoveToError(MessageEnvelope envelope, Exception exception, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task MoveToSkipped(MessageEnvelope envelope, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task Schedule(Guid scheduleToken, MessageEnvelope envelope, DateTimeOffset deliverAt, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("the write failed");

        public Task<bool> CancelScheduled(Guid scheduleToken, CancellationToken cancellationToken) =>
            throw new NotSupportedException("cancel is not supported");
    }
}
