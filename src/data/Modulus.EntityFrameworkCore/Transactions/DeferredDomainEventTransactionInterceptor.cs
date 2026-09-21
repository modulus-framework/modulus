namespace Modulus.EntityFrameworkCore.Transactions;

using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

/// <summary>
/// Bridges explicit database transactions to the deferred domain-event queue.
/// Whenever a <see cref="ModuleDbContext"/>'s transaction commits, any domain
/// events that <see cref="ModuleDbContext.SaveChangesAsync"/> deferred while the
/// transaction was active are drained and dispatched — after the commit, so
/// handlers never observe uncommitted state. On rollback the queue is cleared
/// instead: events whose writes were rolled back must never fire.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists:</b> ModuleDbContext defers dispatch whenever
/// <c>Database.CurrentTransaction</c> is active, but something must drain the
/// scoped queue afterwards. <c>TransactionBehavior</c> drains after its own
/// commits, yet a <em>manual</em> transaction (<c>await db.Database
/// .BeginTransactionAsync()</c> … <c>await tx.CommitAsync()</c>) outside the
/// mediator pipeline had no drainer — events were silently lost when the scope
/// ended. EF Core raises <see cref="IDbTransactionInterceptor"/> callbacks for
/// every commit/rollback path (<c>tx.CommitAsync()</c>,
/// <c>Database.CommitTransactionAsync()</c>, <c>TransactionBehavior</c>'s
/// commits), so this interceptor closes the gap in one place.
/// </para>
/// <para>
/// The interceptor is stateless (everything is reached through
/// <c>eventData.Context</c>), so a single shared instance can be registered in
/// <c>DbContextOptions</c> without leaking per-request state.
/// </para>
/// <para>
/// <b>Multi-context caveat:</b> with several contexts wrapped by
/// <c>TransactionBehavior</c>, events dispatch when the <em>first</em> context
/// commits; a later context's commit failing cannot un-dispatch them. The same
/// non-atomicity caveat that applies to multi-context transactions applies
/// here — prefer the transactional outbox for cross-module consistency.
/// </para>
/// <para>
/// <b>Edge case:</b> a transaction that is neither committed nor rolled back
/// (abandoned via dispose) raises no interceptor callback; its deferred events
/// stay queued until the scope ends or a later drain runs. Its writes were
/// rolled back with it, so treat an abandoned transaction as a bug — always
/// commit or roll back explicitly.
/// </para>
/// </remarks>
public sealed class DeferredDomainEventTransactionInterceptor : DbTransactionInterceptor
{
    /// <summary>Shared stateless instance registered by <c>AddModuleDatabase</c>.</summary>
    public static readonly DeferredDomainEventTransactionInterceptor Instance = new();

    private DeferredDomainEventTransactionInterceptor()
    {
    }

    /// <inheritdoc />
    public override async Task TransactionCommittedAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is ModuleDbContext moduleContext)
            await moduleContext.DrainDeferredDomainEventsAsync(cancellationToken);

        await base.TransactionCommittedAsync(transaction, eventData, cancellationToken);
    }

    /// <inheritdoc />
    public override Task TransactionRolledBackAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        (eventData.Context as ModuleDbContext)?.ClearDeferredDomainEvents();
        return base.TransactionRolledBackAsync(transaction, eventData, cancellationToken);
    }

    /// <inheritdoc />
    public override void TransactionCommitted(
        DbTransaction transaction,
        TransactionEndEventData eventData)
    {
        // Manual sync commits are rare in this async-first framework, but
        // dropping the events silently would be worse than blocking here.
        // EF's sync interceptor callback cannot await; blocking is safe on
        // ASP.NET Core hosts (no SynchronizationContext to deadlock against).
#pragma warning disable VSTHRD002 // sync EF interceptor callback; no SynchronizationContext in Modulus hosts
        (eventData.Context as ModuleDbContext)
            ?.DrainDeferredDomainEventsAsync(CancellationToken.None)
            .GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
        base.TransactionCommitted(transaction, eventData);
    }

    /// <inheritdoc />
    public override void TransactionRolledBack(
        DbTransaction transaction,
        TransactionEndEventData eventData)
    {
        (eventData.Context as ModuleDbContext)?.ClearDeferredDomainEvents();
        base.TransactionRolledBack(transaction, eventData);
    }
}
