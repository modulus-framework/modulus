using System.Diagnostics.Metrics;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Modulus.Messaging.Inbox.EntityFrameworkCore;
using Modulus.Messaging.Transports.InMemory;
using Xunit;

namespace Modulus.Messaging.Inbox.EntityFrameworkCore.Tests;

[Trait("Category", "Unit")]
public sealed class InboxTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private ServiceProvider _provider = null!;
    private MeterListener _listener = null!;
    private long _duplicates;

    public Task InitializeAsync()
    {
        _listener = new MeterListener();
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == "Modulus.Messaging" && instrument.Name == "messaging.inbox.duplicates")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>((_, measurement, _, _) => Interlocked.Add(ref _duplicates, measurement));
        _listener.Start();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _listener.Dispose();
        if (_provider is not null)
        {
            foreach (var host in _provider.GetServices<IHostedService>())
            {
                await host.StopAsync(CancellationToken.None);
            }

            await _provider.DisposeAsync();
        }

        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }
    }

    [Fact]
    public async Task FR_11_a_message_delivered_twice_changes_data_once_and_counts_the_duplicate()
    {
        await StartAsync(b => b.AddConsumer<RecordingConsumer, OrderPlaced>("orders"));
        var envelope = Envelope("orders", new OrderPlaced(Guid.NewGuid()), typeof(RecordingConsumer));

        await Transport().Send(envelope, CancellationToken.None);
        await Transport().Send(envelope, CancellationToken.None);

        (await WaitUntil(() => Volatile.Read(ref _duplicates) == 1)).Should().BeTrue();
        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InboxTestDb>();
        (await db.ConsumedOrders.CountAsync()).Should().Be(1);
        (await db.Set<InboxRecord>().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task RULE_06_a_failed_attempt_rolls_back_its_writes_and_its_inbox_record()
    {
        await StartAsync(b => b.AddConsumer<WriteThenFailConsumer, OrderPlaced>("orders"));
        var envelope = Envelope("orders", new OrderPlaced(Guid.NewGuid()), typeof(WriteThenFailConsumer));

        await Transport().Send(envelope, CancellationToken.None);

        (await WaitUntil(() => Transport().Errors("orders").Count == 1)).Should().BeTrue();
        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InboxTestDb>();
        (await db.ConsumedOrders.CountAsync()).Should().Be(0);
        (await db.Set<InboxRecord>().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task RULE_04_purging_removes_only_records_older_than_the_window()
    {
        await StartAsync(b => b.AddConsumer<RecordingConsumer, OrderPlaced>("orders"));
        await using (var scope = _provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<InboxTestDb>();
            db.Set<InboxRecord>().Add(new InboxRecord { MessageId = Guid.NewGuid(), ConsumerKey = "old", ReceivedAt = DateTime.UtcNow.AddDays(-8) });
            db.Set<InboxRecord>().Add(new InboxRecord { MessageId = Guid.NewGuid(), ConsumerKey = "new", ReceivedAt = DateTime.UtcNow.AddDays(-1) });
            await db.SaveChangesAsync();
        }

        await using var purge = _provider.CreateAsyncScope();
        var removed = await InboxMaintenance.PurgeExpiredAsync(
            purge.ServiceProvider.GetRequiredService<InboxTestDb>(), TimeSpan.FromDays(7), CancellationToken.None);

        removed.Should().Be(1);
        (await purge.ServiceProvider.GetRequiredService<InboxTestDb>().Set<InboxRecord>().Select(r => r.ConsumerKey).ToListAsync())
            .Should().ContainSingle().Which.Should().Be("new");
    }

    private async Task StartAsync(Action<Modulus.Messaging.MessagingBusBuilder> consumers)
    {
        // The keep-alive connection holds the shared in-memory database open. Each DbContext opens its own
        // connection to it: one SqliteConnection is not thread-safe, and the consumer and the test read
        // the database from different threads.
        var connectionString = $"DataSource=file:inbox-{Guid.NewGuid():N}?mode=memory&cache=shared";
        _connection = new SqliteConnection(connectionString);
        await _connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddDbContext<InboxTestDb>(o => o.UseSqlite(connectionString));
        services.AddModulusMessaging(b =>
        {
            consumers(b);
            b.UseInMemory();
        });
        services.AddModulusMessagingEntityFrameworkInbox<InboxTestDb>();

        _provider = services.BuildServiceProvider();
        await using (var scope = _provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<InboxTestDb>().Database.EnsureCreatedAsync();
        }

        foreach (var host in _provider.GetServices<IHostedService>())
        {
            await host.StartAsync(CancellationToken.None);
        }
    }

    private Modulus.Messaging.Transports.InMemory.InMemoryTransport Transport() =>
        (Modulus.Messaging.Transports.InMemory.InMemoryTransport)_provider.GetRequiredService<Modulus.Messaging.IMessageTransport>();

    [Fact]
    public async Task D7_the_inbox_row_names_the_company_the_message_was_handled_for()
    {
        await StartAsync(b => b.AddConsumer<RecordingConsumer, OrderPlaced>("orders"));
        var headers = new Dictionary<string, string> { [Modulus.Messaging.MessagingHeaders.TenantId] = "company-a" };
        var envelope = Envelope("orders", new OrderPlaced(Guid.NewGuid()), typeof(RecordingConsumer)) with { Headers = headers };

        await Transport().Send(envelope, CancellationToken.None);

        // Generous: under a full parallel test run the first consume can take well over 5 seconds.
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(30);
        while (await CountRowsAsync() != 1 && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        var dead = Transport().Errors("orders");
        dead.Should().BeEmpty("the consumer should not dead-letter the message: " +
            string.Join(" | ", dead.Select(d => d.Exception?.ToString())));

        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InboxTestDb>();
        (await db.Set<InboxRecord>().SingleAsync()).TenantId.Should().Be("company-a");
    }

    private async Task<int> CountRowsAsync()
    {
        await using var scope = _provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<InboxTestDb>().Set<InboxRecord>().CountAsync();
    }

    private static Modulus.Messaging.MessageEnvelope Envelope(string endpoint, object message, Type consumer) =>
        new(Guid.NewGuid(), endpoint, message, new Dictionary<string, string>(), consumer.FullName);

    private static async Task<bool> WaitUntil(Func<bool> condition)
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
