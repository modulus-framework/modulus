using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Modulus.Messaging.Outbox.EntityFrameworkCore;
using Modulus.Messaging.Transports.InMemory;
using Xunit;

namespace Modulus.Messaging.Outbox.EntityFrameworkCore.Tests;

[Trait("Category", "Unit")]
public sealed class ScheduledOutboxTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private ServiceProvider _provider = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection($"DataSource=file:scheduled-{Guid.NewGuid():N}?mode=memory&cache=shared");
        await _connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddSingleton<DeliveredLog>();
        services.AddDbContext<OutboxTestDb>(o => o.UseSqlite(_connection));
        services.AddModulusMessaging(b => b
            .AddConsumer<OrderPlacedConsumer, OrderPlaced>("orders")
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
    public async Task A_scheduled_message_in_the_outbox_is_sent_only_once_it_is_due()
    {
        var orderId = Guid.NewGuid();
        var dueAt = DateTimeOffset.UtcNow.AddMilliseconds(1500);
        await using (var scope = _provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OutboxTestDb>();
            var scheduler = scope.ServiceProvider.GetRequiredService<IMessageScheduler>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            await scheduler.ScheduleSend(new Uri("queue:orders"), dueAt, new OrderPlaced(orderId));
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        // A dispatch that starts before the due time must send nothing. The clock is read before the call, so the
        // dispatch's own clock is never later than this one (a cold start can take longer than the delay itself).
        var startedBeforeDue = DateTimeOffset.UtcNow < dueAt;
        var early = await DispatchOnce();
        if (startedBeforeDue)
        {
            early.Should().Be(0, "the message is not due yet");
        }

        var untilDue = dueAt - DateTimeOffset.UtcNow;
        if (untilDue > TimeSpan.Zero)
        {
            await Task.Delay(untilDue + TimeSpan.FromMilliseconds(50));
        }

        (await DispatchOnce()).Should().Be(1);

        var log = _provider.GetRequiredService<DeliveredLog>();
        (await WaitForAsync(() => log.Orders.Count == 1)).Should().BeTrue();
        log.Orders.Should().ContainSingle().Which.Should().Be(orderId);
    }

    [Fact]
    public async Task A_scheduled_message_in_a_rolled_back_transaction_is_never_stored()
    {
        await using (var scope = _provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OutboxTestDb>();
            var scheduler = scope.ServiceProvider.GetRequiredService<IMessageScheduler>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            await scheduler.ScheduleSend(new Uri("queue:orders"), DateTimeOffset.UtcNow, new OrderPlaced(Guid.NewGuid()));
            await db.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        await using var check = _provider.CreateAsyncScope();
        (await check.ServiceProvider.GetRequiredService<OutboxTestDb>().Set<OutboxRecord>().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task A_scheduled_message_cancelled_before_it_is_due_is_never_sent()
    {
        Guid token;
        await using (var scope = _provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OutboxTestDb>();
            var scheduler = scope.ServiceProvider.GetRequiredService<IMessageScheduler>();
            token = await scheduler.ScheduleSend(new Uri("queue:orders"), DateTimeOffset.UtcNow.AddHours(1), new OrderPlaced(Guid.NewGuid()));
            await db.SaveChangesAsync();
        }

        await using (var scope = _provider.CreateAsyncScope())
        {
            var scheduler = scope.ServiceProvider.GetRequiredService<IMessageScheduler>();
            (await scheduler.CancelScheduled(token)).Should().BeTrue();
            (await scheduler.CancelScheduled(token)).Should().BeFalse("the token has nothing left to cancel");
        }

        (await DispatchOnce()).Should().Be(0);
        await using var check = _provider.CreateAsyncScope();
        (await check.ServiceProvider.GetRequiredService<OutboxTestDb>().Set<OutboxRecord>().CountAsync()).Should().Be(0);
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

            await Task.Delay(10);
        }

        return false;
    }
}
