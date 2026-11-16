using FluentAssertions;
using Npgsql;
using Xunit;

namespace Modulus.Messaging.Transports.PostgreSql.Integration.Tests;

/// <summary>Replay of parked rows (FR-09): the rows of <c>endpoint_error</c> move back to the endpoint, counts reset.</summary>
[Trait("Category", "Integration")]
public sealed class ErrorReplayTests(PostgreSqlBroker database) : IClassFixture<PostgreSqlBroker>
{
    [Fact]
    public async Task FR_09_replay_moves_parked_rows_back_to_the_endpoint_with_their_counts_reset()
    {
        var endpoint = $"replay-{Guid.NewGuid():N}";
        await using var transport = new PostgreSqlTransport(new PostgreSqlTransportOptions { ConnectionString = database.ConnectionString });
        // Started with another endpoint, so the table exists and nothing claims the replayed rows.
        await transport.Start(["replay-other-" + Guid.NewGuid().ToString("N")], (_, _) => Task.CompletedTask, CancellationToken.None);

        await using (var insert = database.DataSource().CreateCommand("""
            INSERT INTO messaging_queue (queue, message_id, message_type, consumer_key, body, headers, delivery_count)
            VALUES (@queue, gen_random_uuid(), 'x', NULL, '{}', '{"mod-redelivery-count":"2","mod-delivery-count":"5","keep":"yes"}'::jsonb, 5)
            """))
        {
            insert.Parameters.AddWithValue("queue", endpoint + "_error");
            await insert.ExecuteNonQueryAsync();
        }

        var moved = await transport.ReplayErrors(endpoint, 10, CancellationToken.None);

        moved.Should().Be(1);
        await using var check = database.DataSource().CreateCommand("SELECT delivery_count, headers::text FROM messaging_queue WHERE queue = @queue");
        check.Parameters.AddWithValue("queue", endpoint);
        await using var reader = await check.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        reader.GetInt32(0).Should().Be(0, "a replayed row starts again");
        var headers = reader.GetString(1);
        headers.Should().Contain("keep").And.NotContain("mod-redelivery-count").And.NotContain("mod-delivery-count");
    }
}
