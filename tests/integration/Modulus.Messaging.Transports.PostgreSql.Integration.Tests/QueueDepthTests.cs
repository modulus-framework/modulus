using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using FluentAssertions;
using Modulus.Messaging.Transports.PostgreSql;
using Npgsql;
using Xunit;

namespace Modulus.Messaging.Transports.PostgreSql.Integration.Tests;

/// <summary>The queue-depth gauge (<c>modulus.messaging.queue.depth</c>) counts each queue's rows from the database.</summary>
[Trait("Category", "Integration")]
public sealed class QueueDepthTests(PostgreSqlBroker database) : IClassFixture<PostgreSqlBroker>
{
    [Fact]
    public async Task D9_the_queue_depth_gauge_reports_the_rows_waiting_in_each_queue()
    {
        var queue = $"depth-{Guid.NewGuid():N}";

        var options = new PostgreSqlTransportOptions
        {
            ConnectionString = database.ConnectionString,
            QueueDepthInterval = TimeSpan.FromMilliseconds(200),
        };
        await using var transport = new PostgreSqlTransport(options);
        // Started with another endpoint: nobody claims the rows of `queue`, so their count is what the gauge must report.
        await transport.Start(["depth-consumer-" + Guid.NewGuid().ToString("N")], (_, _) => Task.CompletedTask, CancellationToken.None);

        // The table exists once the transport has started.
        await using (var command = database.DataSource().CreateCommand("""
            INSERT INTO messaging_queue (queue, message_id, message_type, consumer_key, body, headers)
            SELECT @queue, gen_random_uuid(), 'x', NULL, '{}', '{}'::jsonb FROM generate_series(1, 3)
            """))
        {
            command.Parameters.AddWithValue("queue", queue);
            await command.ExecuteNonQueryAsync();
        }

        var depths = new ConcurrentDictionary<string, long>(StringComparer.Ordinal);
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Name == "modulus.messaging.queue.depth")
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, measurement, tags, _) =>
        {
            foreach (var tag in tags)
            {
                if (tag.Key == "messaging.destination.name" && tag.Value is string name)
                {
                    depths[name] = measurement;
                }
            }
        });
        listener.Start();

        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(15);
        while (DateTimeOffset.UtcNow < deadline && !(depths.TryGetValue(queue, out var count) && count == 3))
        {
            listener.RecordObservableInstruments();
            await Task.Delay(100);
        }

        depths.Should().ContainKey(queue).WhoseValue.Should().Be(3, "three rows wait in the queue");
    }
}
