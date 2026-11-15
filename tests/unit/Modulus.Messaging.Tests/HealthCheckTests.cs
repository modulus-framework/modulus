using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace Modulus.Messaging.Tests;

[Trait("Category", "Unit")]
public sealed class HealthCheckTests
{
    [Fact]
    public async Task FR_24_a_transport_without_a_health_capability_reports_healthy_and_says_so()
    {
        var check = new MessagingHealthCheck(new PlainTransport());

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("does not report");
    }

    [Fact]
    public async Task FR_24_a_reachable_broker_reports_healthy()
    {
        var check = new MessagingHealthCheck(new HealthTransport(_ => Task.FromResult(true)));

        (await check.CheckHealthAsync(new HealthCheckContext())).Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task FR_24_an_unreachable_broker_reports_unhealthy()
    {
        var check = new MessagingHealthCheck(new HealthTransport(_ => Task.FromResult(false)));

        (await check.CheckHealthAsync(new HealthCheckContext())).Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task FR_24_a_check_that_throws_reports_unhealthy_with_the_error()
    {
        var failure = new InvalidOperationException("connection refused");
        var check = new MessagingHealthCheck(new HealthTransport(_ => Task.FromException<bool>(failure)));

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Exception.Should().BeSameAs(failure);
    }

    [Fact]
    public async Task FR_24_a_cancelled_check_is_not_reported_as_unhealthy()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var check = new MessagingHealthCheck(new HealthTransport(ct => Task.FromCanceled<bool>(ct)));

        var act = () => check.CheckHealthAsync(new HealthCheckContext(), cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private sealed class PlainTransport : IMessageTransport
    {
        public Task Start(IReadOnlyCollection<string> endpoints, Func<MessageEnvelope, CancellationToken, Task> receive, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task Stop(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task Send(MessageEnvelope envelope, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task Redeliver(MessageEnvelope envelope, TimeSpan delay, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task MoveToError(MessageEnvelope envelope, Exception exception, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task MoveToSkipped(MessageEnvelope envelope, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class HealthTransport(Func<CancellationToken, Task<bool>> availability) : IMessageTransport, IMessageTransportHealth
    {
        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => availability(cancellationToken);
        public Task Start(IReadOnlyCollection<string> endpoints, Func<MessageEnvelope, CancellationToken, Task> receive, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task Stop(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task Send(MessageEnvelope envelope, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task Redeliver(MessageEnvelope envelope, TimeSpan delay, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task MoveToError(MessageEnvelope envelope, Exception exception, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task MoveToSkipped(MessageEnvelope envelope, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
