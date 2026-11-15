using System.Collections.Concurrent;
using Modulus.Messaging;
using Npgsql;
using Xunit;

// The purge removes every parked row of the table, so the tests that wait for parked rows must not run beside it.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Modulus.Messaging.Transports.PostgreSql.Integration.Tests;

[Trait("Category", "Integration")]
public sealed class PurgeParkedTests(PostgreSqlBroker database) : IClassFixture<PostgreSqlBroker>
{
    [Fact]
    public async Task Parked_messages_are_kept_within_the_retention_and_purged_after_it()
    {
        await using var transport = new PostgreSqlTransport(database.Options());
        var endpoint = "purge." + Guid.NewGuid().ToString("N")[..10];
        await transport.Start([endpoint], (_, _) => Task.CompletedTask, CancellationToken.None);
        var envelope = new MessageEnvelope(Guid.NewGuid(), endpoint, new OrderPlaced(Guid.NewGuid()), new Dictionary<string, string>());
        await transport.MoveToError(envelope, new InvalidOperationException("boom"), CancellationToken.None);
        await transport.MoveToSkipped(envelope, CancellationToken.None);
        await Task.Delay(100);

        var kept = await transport.PurgeParkedAsync(TimeSpan.FromHours(1), CancellationToken.None);
        Assert.True(kept == 0, $"a message parked within the retention was purged ({kept})");

        var purged = await transport.PurgeParkedAsync(TimeSpan.Zero, CancellationToken.None);
        Assert.True(purged >= 2, $"expected the two parked rows to be purged, got {purged}");

        await transport.Stop(CancellationToken.None);
    }

    [Fact]
    public async Task A_negative_retention_is_refused()
    {
        await using var transport = new PostgreSqlTransport(database.Options());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => transport.PurgeParkedAsync(TimeSpan.FromSeconds(-1), CancellationToken.None));
    }

    [Fact]
    public async Task The_retention_is_applied_automatically_while_the_transport_runs()
    {
        var options = database.Options();
        options.ParkedRetention = TimeSpan.FromMilliseconds(300);
        options.PurgeInterval = TimeSpan.FromMilliseconds(100);
        var failures = new ConcurrentBag<Exception>();
        options.PurgeFailed = failures.Add;
        await using var transport = new PostgreSqlTransport(options);
        var endpoint = "autopurge." + Guid.NewGuid().ToString("N")[..10];
        await transport.Start([endpoint], (_, _) => Task.CompletedTask, CancellationToken.None);
        await transport.MoveToError(
            new MessageEnvelope(Guid.NewGuid(), endpoint, new OrderPlaced(Guid.NewGuid()), new Dictionary<string, string>()),
            new InvalidOperationException("boom"),
            CancellationToken.None);

        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (await CountRowsAsync($"{endpoint}_error") > 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100);
        }

        Assert.Equal(0, await CountRowsAsync($"{endpoint}_error"));
        Assert.Empty(failures);
        await transport.Stop(CancellationToken.None);
    }

    private async Task<long> CountRowsAsync(string queue)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT count(*) FROM \"public\".\"messaging_queue\" WHERE queue = @queue", connection);
        command.Parameters.AddWithValue("queue", queue);
        return (long)(await command.ExecuteScalarAsync() ?? 0L);
    }
}
