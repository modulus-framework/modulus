namespace Modulus.Outbox.Abstractions;

using System.Diagnostics;
using Modulus.Core.Abstractions;
using Modulus.Events.Abstractions;

/// <summary>
/// Shared factory for outbox rows. Every writer (<c>EfOutboxWriter</c>,
/// <c>ModuleDbContext</c>'s transactional enqueue) builds rows through this
/// single implementation so the stored shape can never drift between call
/// sites — previously the inline path and the writer path disagreed on
/// <see cref="OutboxMessage.CorrelationId"/>.
/// </summary>
public static class OutboxRowFactory
{
    /// <summary>
    /// Creates an <see cref="OutboxMessage"/> for <paramref name="event"/>.
    /// </summary>
    /// <param name="event">The integration event to persist.</param>
    /// <param name="tenantId">The tenant the event belongs to (Guid.Empty for host scope).</param>
    /// <param name="moduleName">
    /// Owning module name, derived from the context/collection name so the row
    /// is attributable in diagnostics.
    /// </param>
    /// <param name="correlationId">
    /// Ambient business correlation id, when one is in scope.
    /// </param>
    /// <param name="serializer">Message serializer (e.g., System.Text.Json).</param>
    /// <param name="causationId">
    /// The id of the message that caused this operation (if handling a consumed event).
    /// Null when the operation originated from an HTTP request or background job.
    /// </param>
    public static OutboxMessage Create(
        IIntegrationEvent @event,
        Guid tenantId,
        string moduleName,
        string? correlationId,
        IMessageSerializer serializer,
        string? causationId = null)
    {
        var activity = Activity.Current;
        return new()
        {
            // Stable transport name (attribute or assembly-independent
            // FullName), NOT AssemblyQualifiedName — an assembly version bump
            // must not orphan unprocessed outbox rows.
            MessageType = IntegrationEventNaming.GetName(@event.GetType()),
            Payload = serializer.Serialize(@event, @event.GetType()),
            TenantId = tenantId,
            ModuleName = moduleName,
            CorrelationId = correlationId,
            CausationId = causationId,
            TraceParent = activity?.Id,
            TraceState = activity?.TraceStateString,
        };
    }
}
