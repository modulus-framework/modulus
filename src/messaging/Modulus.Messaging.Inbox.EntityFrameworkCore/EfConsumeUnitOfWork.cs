using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Modulus.Messaging.Inbox.EntityFrameworkCore;

/// <summary>
/// One DbContext transaction per consume attempt. Commit saves the consumer's writes, the inbox record and the
/// outbox rows, then commits; Rollback discards all of them. If the scope already has a transaction open,
/// the attempt joins it and does not commit or roll it back itself.
/// </summary>
internal sealed class EfConsumeUnitOfWork<TContext>(TContext db) : IConsumeUnitOfWork
    where TContext : DbContext
{
    private IDbContextTransaction? _owned;

    public async Task Begin(CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null)
        {
            _owned = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task Commit(CancellationToken cancellationToken)
    {
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (_owned is not null)
        {
            await _owned.CommitAsync(cancellationToken).ConfigureAwait(false);
            await _owned.DisposeAsync().ConfigureAwait(false);
            _owned = null;
        }
    }

    public async Task Rollback(CancellationToken cancellationToken)
    {
        if (_owned is not null)
        {
            await _owned.RollbackAsync(cancellationToken).ConfigureAwait(false);
            await _owned.DisposeAsync().ConfigureAwait(false);
            _owned = null;
        }
    }
}
