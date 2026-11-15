using Modulus.Messaging;
using Modulus.Messaging.Outbox.Npgsql;
using Npgsql;

namespace Modulus.Messaging.Chaos.Worker;

public sealed record LedgerEntry(Guid Id);

/// <summary>
/// Applies one entry: a row in the ledger, written in the consume transaction. The inbox record of the attempt commits
/// with it, so a redelivered entry is skipped, and a second row for one entry would violate the primary key.
/// </summary>
public sealed class LedgerConsumer(INpgsqlUnitOfWork uow) : IConsumer<LedgerEntry>
{
    public async Task Consume(ConsumeContext<LedgerEntry> context)
    {
        await using (var command = new NpgsqlCommand("INSERT INTO chaos_ledger (message_id) VALUES (@id)", uow.Connection!, uow.Transaction))
        {
            command.Parameters.AddWithValue("id", context.Message.Id);
            await command.ExecuteNonQueryAsync(context.CancellationToken);
        }

        // A little work, so a kill lands inside an attempt as well as between attempts.
        await Task.Delay(Random.Shared.Next(1, 4), context.CancellationToken);
    }
}
