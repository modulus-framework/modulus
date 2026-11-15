using System.Collections.Concurrent;
using System.Diagnostics;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Modulus.Messaging.Transports.InMemory;
using Xunit;

namespace Modulus.Messaging.Outbox.EntityFrameworkCore.Tests;

[Trait("Category", "Unit")]
public sealed class OutboxTraceTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private ServiceProvider _provider = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection($"DataSource=file:outbox-trace-{Guid.NewGuid():N}?mode=memory&cache=shared");
        await _connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddSingleton<DeliveredLog>();
        services.AddDbContext<OutboxTestDb>(o => o.UseSqlite(_connection));
        services.AddModulusMessaging(b => b
            .AddConsumer<OrderPlacedConsumer, OrderPlaced>("orders-trace")
            .UseInMemory());
        services.AddModulusMessagingEntityFrameworkOutbox<OutboxTestDb>(o => o.RunDispatcher = false);

        _provider = services.BuildServiceProvider();
        await using var scope = _provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<OutboxTestDb>().Database.EnsureCreatedAsync();

        foreach (var host in _provider.GetServices<IHostedService>())
        {
            await host.StartAsync(CancellationToken.None);
        }
    }

    public async Task DisposeAsync()
    {
        foreach (var host in _provider.GetServices<IHostedService>())
        {
            await host.StopAsync(CancellationToken.None);
        }

        await _provider.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task FR_24_an_outbox_message_keeps_its_trace_until_the_dispatcher_sends_it()
    {
        var finished = new ConcurrentQueue<Activity>();
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Modulus.Messaging",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = finished.Enqueue,
        };
        ActivitySource.AddActivityListener(listener);
        using var _ = listener;

        var orderId = Guid.NewGuid();
        using var root = new Activity("checkout").Start();
        await using (var scope = _provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OutboxTestDb>();
            var bus = scope.ServiceProvider.GetRequiredService<IBus>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            await bus.Publish(new OrderPlaced(orderId));
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        // The row is stored with its trace context; the dispatcher runs later, in a different scope.
        await DispatchOnce();
        // Other tests in this project run in parallel and publish too, so every lookup is limited to this test's trace.
        (await WaitForAsync(() => finished.Any(a => a.Kind == ActivityKind.Consumer && a.DisplayName == "process orders-trace" && a.TraceId == root.TraceId))).Should().BeTrue();
        var producer = finished.Single(a => a.Kind == ActivityKind.Producer && a.DisplayName == "publish OrderPlaced" && a.TraceId == root.TraceId);
        var consumer = finished.Single(a => a.Kind == ActivityKind.Consumer && a.DisplayName == "process orders-trace" && a.TraceId == root.TraceId);

        producer.TraceId.Should().Be(root.TraceId, "the publish runs inside the caller's trace");
        producer.ParentSpanId.Should().Be(root.SpanId);
        consumer.TraceId.Should().Be(root.TraceId, "the consumer continues the trace across the outbox");
        consumer.ParentSpanId.Should().Be(producer.SpanId, "the consumer is a child of the publish span, not of the dispatcher");
        consumer.GetTagItem("messaging.system").Should().Be("in-memory");
    }

    private async Task<int> DispatchOnce()
    {
        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OutboxTestDb>();
        var transport = scope.ServiceProvider.GetRequiredService<IMessageTransport>();
        return await OutboxDispatch.DispatchOnceAsync(db, transport, batchSize: 100, leaseDuration: TimeSpan.FromSeconds(30), CancellationToken.None);
    }

    private static async Task<bool> WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(5);
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
