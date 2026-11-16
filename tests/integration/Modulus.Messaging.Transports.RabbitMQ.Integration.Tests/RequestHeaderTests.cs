using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Modulus.Messaging.Transports.RabbitMQ.Integration.Tests;

/// <summary>A saga request (SendRequest) on a real broker: the id and reply endpoint arrive, and a failure comes back as a fault.</summary>
[Trait("Category", "Integration")]
public sealed class RequestHeaderTests(RabbitMqBroker broker) : IClassFixture<RabbitMqBroker>
{
    [Fact]
    public async Task A_request_keeps_its_id_and_reply_endpoint_and_a_consumer_failure_returns_a_fault_to_the_reply_endpoint()
    {
        var endpoint = $"request-{Guid.NewGuid():N}";
        var replyEndpoint = $"request-reply-{Guid.NewGuid():N}";
        var seen = new ConcurrentQueue<(string? RequestId, string? ReplyTo)>();
        var faults = new ConcurrentQueue<RequestFault>();
        var services = new ServiceCollection();
        services.AddSingleton(seen);
        services.AddSingleton(faults);
        services.AddModulusMessaging(b =>
        {
            b.AddConsumer<FailingQuoteConsumer, RequestedQuote>(endpoint, o => o.Retry = RetryPolicy.Immediate(1));
            b.AddConsumer<FaultRecorder, RequestFault>(replyEndpoint);
            b.UseRabbitMq(o =>
            {
                var options = broker.Options();
                o.HostName = options.HostName;
                o.Port = options.Port;
                o.UserName = options.UserName;
                o.Password = options.Password;
                o.StopTimeout = options.StopTimeout;
            });
        });

        await using var provider = services.BuildServiceProvider();
        var host = provider.GetServices<IHostedService>().Single();
        await host.StartAsync(CancellationToken.None);
        try
        {
            var bus = provider.GetRequiredService<IBus>();
            var requestId = Guid.NewGuid();
            await bus.SendRequest(new Uri($"queue:{endpoint}"), new RequestedQuote(Guid.NewGuid()), requestId, replyEndpoint);

            var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(60);
            while (faults.IsEmpty && DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(50);
            }

            seen.Should().NotBeEmpty("the consumer received the request");
            seen.First().RequestId.Should().Be(requestId.ToString("N"));
            seen.First().ReplyTo.Should().Be(replyEndpoint);

            faults.Should().ContainSingle("the failure came back to the reply endpoint");
            var fault = faults.Single();
            fault.RequestId.Should().Be(requestId);
            fault.ExceptionType.Should().Be(typeof(InvalidOperationException).FullName);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }
    }
}

public sealed record RequestedQuote(Guid OrderId);

/// <summary>Records the request headers it receives, then fails, so the bus parks the request and sends its fault.</summary>
public sealed class FailingQuoteConsumer(ConcurrentQueue<(string? RequestId, string? ReplyTo)> seen) : IConsumer<RequestedQuote>
{
    public Task Consume(ConsumeContext<RequestedQuote> context)
    {
        context.Headers.TryGetValue(MessagingHeaders.RequestId, out var requestId);
        context.Headers.TryGetValue(MessagingHeaders.ReplyTo, out var replyTo);
        seen.Enqueue((requestId, replyTo));
        throw new InvalidOperationException("no price for this order");
    }
}

public sealed class FaultRecorder(ConcurrentQueue<RequestFault> faults) : IConsumer<RequestFault>
{
    public Task Consume(ConsumeContext<RequestFault> context)
    {
        faults.Enqueue(context.Message);
        return Task.CompletedTask;
    }
}
