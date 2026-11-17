using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Modulus.Messaging.Sagas.EntityFrameworkCore;

/// <summary>
/// Stores saga instances in the application's DbContext. A write checks the version in the database, in one statement
/// (UPDATE ... WHERE Version = read version), so two writers cannot both succeed on the same version.
/// </summary>
public sealed class EfSagaRepository<TSaga>(DbContext db) : ISagaRepository<TSaga>
    where TSaga : SagaInstance
{
    private static readonly string SagaType = typeof(TSaga).FullName ?? typeof(TSaga).Name;

    public async Task<TSaga?> FindAsync(Guid correlationId, CancellationToken cancellationToken)
    {
        var record = await Records()
            .AsNoTracking()
            .SingleOrDefaultAsync(r => r.CorrelationId == correlationId, cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return null;
        }

        // The version column is authoritative: the JSON was written before the version was set on the instance.
        var saga = JsonSerializer.Deserialize<TSaga>(record.Data);
        if (saga is not null)
        {
            saga.Version = record.Version;
        }

        return saga;
    }

    public async Task SaveAsync(TSaga saga, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(saga);
        var data = JsonSerializer.Serialize(saga);

        if (saga.Version == 0)
        {
            // An instance this context already tracks is a second insert of the same saga: a conflict, not a tracking error.
            if (db.ChangeTracker.Entries<SagaRecord>().Any(e => e.State != EntityState.Detached
                    && e.Entity.SagaType == SagaType && e.Entity.CorrelationId == saga.CorrelationId))
            {
                throw new SagaConcurrencyException(typeof(TSaga), saga.CorrelationId);
            }

            // A new instance: the primary key refuses a second one, so a racing initiation is a conflict.
            var record = new SagaRecord
            {
                SagaType = SagaType,
                CorrelationId = saga.CorrelationId,
                Version = 1,
                Data = data,
            };
            db.Set<SagaRecord>().Add(record);
            try
            {
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateException)
            {
                // Leave no unsaved row in the context: a later save on it must not write this one.
                db.Entry(record).State = EntityState.Detached;
                throw new SagaConcurrencyException(typeof(TSaga), saga.CorrelationId);
            }

            saga.Version = 1;
            return;
        }

        var updated = await Records()
            .Where(r => r.CorrelationId == saga.CorrelationId && r.Version == saga.Version)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(r => r.Version, saga.Version + 1)
                    .SetProperty(r => r.Data, data),
                cancellationToken)
            .ConfigureAwait(false);
        if (updated == 0)
        {
            throw new SagaConcurrencyException(typeof(TSaga), saga.CorrelationId);
        }

        saga.Version += 1;
    }

    public async Task DeleteAsync(TSaga saga, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(saga);
        if (saga.Version == 0)
        {
            // Never stored: a saga that initiates and completes in one step leaves nothing behind.
            return;
        }

        var deleted = await Records()
            .Where(r => r.CorrelationId == saga.CorrelationId && r.Version == saga.Version)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
        if (deleted == 0)
        {
            throw new SagaConcurrencyException(typeof(TSaga), saga.CorrelationId);
        }
    }

    private IQueryable<SagaRecord> Records() => db.Set<SagaRecord>().Where(r => r.SagaType == SagaType);
}
