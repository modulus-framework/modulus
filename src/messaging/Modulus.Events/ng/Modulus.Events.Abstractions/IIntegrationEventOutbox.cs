namespace Modulus.Events.Abstractions;

/// <summary>
/// Enqueues an integration event so that it is persisted in the same
/// database transaction as the domain data. Implementations (e.g.
/// <c>EfOutboxWriter</c>) add an outbox row to the EF Core change tracker;
/// the row is committed atomically with the next <c>SaveChanges</c> call.
/// </summary>
/// <remarks>
/// Call <see cref="Enqueue"/> BEFORE <c>DbContext.SaveChangesAsync</c> so
/// the outbox row and the domain writes share one transaction.  The
/// <c>OutboxProcessor</c> background service then relays the row to the
/// event bus at-least-once.
/// </remarks>
public interface IIntegrationEventOutbox
{
    /// <summary>
    /// Adds the event to the current unit of work (change tracker).
    /// Synchronous because it performs no I/O — persistence happens on
    /// the next <c>SaveChanges</c>.
    /// </summary>
    void Enqueue(IIntegrationEvent @event);
}

/// <summary>
/// Default no-op implementation. Registered by <c>AddModuleDatabase</c>
/// so that <c>ModuleDbContext</c> can always resolve the outbox, even when
/// the application has not configured one.  Replaced by
/// <c>EfOutboxWriter</c> when <c>AddOutbox</c> is called.
/// </summary>
public sealed class NullIntegrationEventOutbox : IIntegrationEventOutbox
{
    public void Enqueue(IIntegrationEvent @event) { /* no-op */ }
}
