using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Modulus.Messaging.Transports.RabbitMQ.Integration.Tests;

/// <summary>Broker outage and recovery (NFR-02). Kept in its own class so it can stop its own broker.</summary>
[Trait("Category", "Integration")]
public sealed class RabbitMqOutageTests(RabbitMqBroker broker) : IClassFixture<RabbitMqBroker>
{
    [Fact]
    public async Task NFR_02_the_bus_recovers_when_the_broker_restarts_without_a_host_restart()
    {
        var endpoint = $"outage-{Guid.NewGuid():N}";
        var received = new Received();
        var services = new ServiceCollection();
        services.AddSingleton(received);
        services.AddModulusMessaging(b =>
        {
            b.AddConsumer<RecordingConsumer, OrderPlaced>(endpoint);
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
        var bus = provider.GetRequiredService<IBus>();

        var before = Guid.NewGuid();
        await bus.Send(new Uri($"queue:{endpoint}"), new OrderPlaced(before));
        (await WaitUntil(() => received.OrderIds.Count == 1)).Should().BeTrue();

        await broker.StopBrokerAsync();

        // While the broker is down a publish fails with an error; it must not hang or be reported as sent.
        var lost = Guid.NewGuid();
        var during = async () => await bus.Send(new Uri($"queue:{endpoint}"), new OrderPlaced(lost));
        await during.Should().ThrowAsync<Exception>();

        await broker.StartBrokerAsync();

        // Automatic recovery reconnects and re-declares the topology; the consumer comes back by itself.
        var after = Guid.NewGuid();
        await SendWhenRecovered(bus, endpoint, after);

        (await WaitUntil(() => received.OrderIds.Contains(after))).Should().BeTrue();
        received.OrderIds.Should().Contain(before);
        received.OrderIds.Should().NotContain(lost, "a message whose publish failed was never accepted by the broker");

        await host.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task FR_24_the_health_check_reports_unhealthy_while_the_broker_is_down_and_healthy_after_it_returns()
    {
        var services = new ServiceCollection();
        services.AddModulusMessaging(b => b.UseRabbitMq(o =>
        {
            var options = broker.Options();
            o.HostName = options.HostName;
            o.Port = options.Port;
            o.UserName = options.UserName;
            o.Password = options.Password;
        }));
        await using var provider = services.BuildServiceProvider();
        var host = provider.GetServices<IHostedService>().Single();
        await host.StartAsync(CancellationToken.None);
        var check = new MessagingHealthCheck(provider.GetRequiredService<IMessageTransport>());

        (await check.CheckHealthAsync(new HealthCheckContext())).Status.Should().Be(HealthStatus.Healthy);

        await broker.StopBrokerAsync();
        (await check.CheckHealthAsync(new HealthCheckContext())).Status.Should().Be(HealthStatus.Unhealthy);

        await broker.StartBrokerAsync();
        var recovered = false;
        for (var attempt = 0; attempt < 120 && !recovered; attempt++)
        {
            recovered = (await check.CheckHealthAsync(new HealthCheckContext())).Status == HealthStatus.Healthy;
            if (!recovered)
            {
                await Task.Delay(500);
            }
        }

        recovered.Should().BeTrue("automatic recovery reconnects, and the check reports healthy again");
        await host.StopAsync(CancellationToken.None);
    }

    private static async Task SendWhenRecovered(IBus bus, string endpoint, Guid orderId)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(60);
        while (true)
        {
            try
            {
                await bus.Send(new Uri($"queue:{endpoint}"), new OrderPlaced(orderId));
                return;
            }
            catch (Exception) when (DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(500);
            }
        }
    }

    private static async Task<bool> WaitUntil(Func<bool> condition)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(60);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(50);
        }

        return condition();
    }
}
