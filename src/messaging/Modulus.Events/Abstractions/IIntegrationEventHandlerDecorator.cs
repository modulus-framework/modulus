namespace Modulus.Events.Abstractions;

/// <summary>
/// Optional hook that lets a feature package (e.g. the inbox) wrap a resolved
/// <see cref="IIntegrationEventHandler{TEvent}"/> instance before it is invoked,
/// without the dispatcher needing to reference that package. A feature that
/// wants to intercept dispatch registers exactly one implementation (via
/// <c>TryAddSingleton</c> so repeated registration — e.g. one <c>AddInbox</c>
/// call per module — stays idempotent); <see cref="IntegrationEventDispatcher"/>
/// and <see cref="InProcessModuleBus"/> both resolve it as optional
/// (<c>GetService</c>, not <c>GetRequiredService</c>), so nothing changes for
/// either dispatch path when no such feature is registered.
/// </summary>
/// <remarks>
/// <para>
/// This exists to keep the wrapping decision at <b>dispatch time</b> rather
/// than at DI-registration time. The previous design (removing and re-adding
/// <c>IServiceDescriptor</c> entries for every <c>IIntegrationEventHandler&lt;T&gt;</c>
/// found at the moment a feature's <c>AddXyz(...)</c> ran) only worked if every
/// handler was already registered before that call — which depends entirely on
/// the order two unrelated calls (typically <c>AddModulusEvents(...)</c> and
/// each module's <c>AddInbox&lt;TContext&gt;(...)</c>) happen to appear in
/// <c>Program.cs</c>. Handlers resolved fresh on every dispatch instead of
/// captured once by registration order, so this is order-independent and safe
/// to call any number of times.
/// </para>
/// <para>
/// Deliberately non-generic (reflection-friendly): both call sites already
/// operate on the closed <c>IIntegrationEventHandler&lt;TEvent&gt;</c> interface
/// as <see cref="object"/> (one via generics, one via <see cref="Type"/>
/// reflection), so the handler passed to <see cref="Decorate"/> is guaranteed
/// by the caller to implement <c>IIntegrationEventHandler&lt;TEvent&gt;</c> for
/// the given event type, and the returned object must implement that same
/// closed interface so the caller's compiled/generic invoker keeps working
/// unchanged against whatever is returned.
/// </para>
/// </remarks>
public interface IIntegrationEventHandlerDecorator
{
    /// <summary>
    /// Wraps <paramref name="handler"/> (an
    /// <c>IIntegrationEventHandler&lt;TEvent&gt;</c> for <paramref name="eventType"/>)
    /// and returns the object to invoke instead. May return
    /// <paramref name="handler"/> unchanged.
    /// </summary>
    object Decorate(IServiceProvider services, Type eventType, object handler);
}
