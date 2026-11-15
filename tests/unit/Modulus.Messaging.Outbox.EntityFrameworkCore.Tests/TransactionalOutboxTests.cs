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
public sealed class TransactionalOutboxTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private ServiceProvider _provider = null!;

    public async Task InitializeAsync()
    {
        // A shared in-memory SQLite database that lives as long as the connection.
        _connection = new SqliteConnection($"DataSource=file:outbox-{Guid.NewGuid():N}?mode=memory&cache=shared");
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

        // Start the bus only; the test drives the outbox dispatch itself.
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
    public async Task FR_10_a_rolled_back_transaction_sends_nothing()
    {
        var orderId = Guid.NewGuid();
        await using (var scope = _provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OutboxTestDb>();
            var bus = scope.ServiceProvider.GetRequiredService<IBus>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            await bus.Publish(new OrderPlaced(orderId));
            await db.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        var sent = await DispatchOnce();

        sent.Should().Be(0);
        _provider.GetRequiredService<DeliveredLog>().Orders.Should().BeEmpty();
        await using var check = _provider.CreateAsyncScope();
        var remaining = await check.ServiceProvider.GetRequiredService<OutboxTestDb>().Set<OutboxRecord>().ToListAsync();
        remaining.Should().BeEmpty();
    }

    [Fact]
    public async Task FR_10_a_committed_transaction_sends_once_and_then_clears_the_outbox()
    {
        var orderId = Guid.NewGuid();
        await using (var scope = _provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OutboxTestDb>();
            var bus = scope.ServiceProvider.GetRequiredService<IBus>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            await bus.Publish(new OrderPlaced(orderId));
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        (await DispatchOnce()).Should().Be(1);
        var log = _provider.GetRequiredService<DeliveredLog>();
        (await MessagingTestHarnessWait(() => log.Orders.Count == 1)).Should().BeTrue();
        log.Orders.Should().ContainSingle().Which.Should().Be(orderId);

        // A second round finds nothing: the row was deleted after its send.
        (await DispatchOnce()).Should().Be(0);
        await Task.Delay(50);
        log.Orders.Should().ContainSingle();
    }

    [Fact]
    public async Task RULE_01_a_message_whose_type_no_longer_exists_is_parked_not_dropped()
    {
        await using (var scope = _provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OutboxTestDb>();
            db.Set<OutboxRecord>().Add(new OutboxRecord
            {
                MessageId = Guid.NewGuid(),
                Endpoint = "orders",
                MessageType = "Some.Type.That.Was.Removed, Nowhere",
                Body = "{}",
                Headers = "{}",
                EnqueuedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        (await DispatchOnce()).Should().Be(0);

        await using var check = _provider.CreateAsyncScope();
        var parked = await check.ServiceProvider.GetRequiredService<OutboxTestDb>()
            .Set<OutboxRecord>().SingleAsync();
        parked.Parked.Should().BeTrue();
    }

    private async Task<int> DispatchOnce()
    {
        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OutboxTestDb>();
        var transport = scope.ServiceProvider.GetRequiredService<Modulus.Messaging.IMessageTransport>();
        return await OutboxDispatch.DispatchOnceAsync(db, transport, batchSize: 100, leaseDuration: TimeSpan.FromSeconds(30), CancellationToken.None);
    }

    private static async Task<bool> MessagingTestHarnessWait(Func<bool> condition)
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

        return condition();
    }
}
