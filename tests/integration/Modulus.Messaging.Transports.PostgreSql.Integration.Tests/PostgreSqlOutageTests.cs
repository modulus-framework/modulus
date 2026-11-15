using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Modulus.Messaging.Transports.PostgreSql.Integration.Tests;

/// <summary>
/// Database outage and recovery (NFR-02). Kept in its own class with its own database, because it stops the container:
/// the other tests of a shared database would fail after it.
/// </summary>
[Trait("Category", "Integration")]
public sealed class PostgreSqlOutageTests(PostgreSqlBroker database) : IClassFixture<PostgreSqlBroker>
{
    [Fact]
    public async Task NFR_02_delivery_resumes_when_the_database_restarts()
    {
        var endpoint = $"outage-{Guid.NewGuid():N}";
        var received = new Received();

        var services = new ServiceCollection();
        services.AddSingleton(received);
        services.AddModulusMessaging(b =>
        {
            b.AddConsumer<RecordingConsumer, OrderPlaced>(endpoint);
            b.UsePostgreSql(o =>
            {
                var options = database.Options();
                o.ConnectionString = options.ConnectionString;
                o.StopTimeout = options.StopTimeout;
                o.LeaseDuration = options.LeaseDuration;
                o.PollInterval = options.PollInterval;
            });
        });

        await using var provider = services.BuildServiceProvider();
        var host = provider.GetServices<IHostedService>().Single();
        await host.StartAsync(CancellationToken.None);
        var bus = provider.GetRequiredService<IBus>();

        var before = Guid.NewGuid();
        await bus.Send(new Uri($"queue:{endpoint}"), new OrderPlaced(before));
        (await WaitUntil(() => received.OrderIds.Count == 1)).Should().BeTrue();

        await database.StopDatabaseAsync();

        // While the database is down a send fails with an error; the bus never reports it as sent.
        var during = async () => await bus.Send(new Uri($"queue:{endpoint}"), new OrderPlaced(Guid.NewGuid()));
        await during.Should().ThrowAsync<Exception>();

        await database.StartDatabaseAsync();

        var after = Guid.NewGuid();
        await SendWhenRecovered(bus, endpoint, after);
        (await WaitUntil(() => received.OrderIds.Contains(after), TimeSpan.FromSeconds(60))).Should().BeTrue();
        received.OrderIds.Should().Contain(before);

        await host.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task FR_24_the_health_check_reports_unhealthy_while_the_database_is_down_and_healthy_after_it_returns()
    {
        var services = new ServiceCollection();
        services.AddModulusMessaging(b => b.UsePostgreSql(o =>
        {
            var options = database.Options();
            o.ConnectionString = options.ConnectionString;
        }));
        await using var provider = services.BuildServiceProvider();
        var check = new MessagingHealthCheck(provider.GetRequiredService<IMessageTransport>());

        (await check.CheckHealthAsync(new HealthCheckContext())).Status.Should().Be(HealthStatus.Healthy);

        await database.StopDatabaseAsync();
        (await check.CheckHealthAsync(new HealthCheckContext())).Status.Should().Be(HealthStatus.Unhealthy);

        await database.StartDatabaseAsync();
        var recovered = false;
        for (var attempt = 0; attempt < 120 && !recovered; attempt++)
        {
            recovered = (await check.CheckHealthAsync(new HealthCheckContext())).Status == HealthStatus.Healthy;
            if (!recovered)
            {
                await Task.Delay(500);
            }
        }

        recovered.Should().BeTrue("the check reports healthy again once the database accepts queries");
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

    private static async Task<bool> WaitUntil(Func<bool> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTimeOffset.UtcNow + (timeout ?? TimeSpan.FromSeconds(15));
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(20);
        }

        return condition();
    }
}
