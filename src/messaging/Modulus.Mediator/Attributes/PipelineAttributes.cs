namespace Modulus.Mediator.Abstractions.Attributes;

[AttributeUsage(AttributeTargets.Class)]
public sealed class SkipValidationAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Class)]
public sealed class SkipTransactionAttribute : Attribute { }

/// <summary>
/// Declares the exact <c>DbContext</c> types a command touches, so
/// <c>TransactionBehavior</c> wraps <b>only</b> those in a transaction instead of
/// opening one on every registered module context. In a modular monolith with N
/// modules, the un-scoped default would acquire N connections and begin N
/// transactions for a command that writes to one — this attribute keeps the cost
/// proportional to what the handler actually uses.
/// <code>
/// [Transactional(typeof(CatalogDbContext))]
/// public sealed record CreateProductCommand(...) : ICommand&lt;Guid&gt;;
/// </code>
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class TransactionalAttribute(params Type[] contexts) : Attribute
{
    /// <summary>The <c>DbContext</c> types this command's handler writes to.</summary>
    public Type[] Contexts { get; } = contexts;
}

/// <summary>
/// Controls which <c>DbContext</c>s <c>TransactionBehavior</c> wraps for a command
/// that carries no <see cref="TransactionalAttribute"/>.
/// </summary>
public enum TransactionMode
{
    /// <summary>
    /// Default. Wrap the single registered context when exactly one exists (the
    /// common case — fully atomic); when multiple contexts are registered, wrap
    /// none (each context's <c>SaveChangesAsync</c> is still atomic on its own,
    /// and cross-connection wrapping was only pseudo-atomic anyway). Declare
    /// intent with <see cref="TransactionalAttribute"/> to wrap specific contexts.
    /// </summary>
    TouchedOrSingle,

    /// <summary>
    /// Legacy behaviour: wrap <b>every</b> registered <c>DbContext</c> in its own
    /// transaction. Opt in only if you relied on the old fan-out.
    /// </summary>
    AllContexts,
}

/// <summary>
/// Immutable snapshot of the transaction policy, registered as a singleton so
/// <c>TransactionBehavior</c> can read it without depending on mutable options.
/// </summary>
public sealed record TransactionRuntimeOptions(TransactionMode Mode);

[AttributeUsage(AttributeTargets.Class)]
public sealed class CacheForAttribute(int seconds) : Attribute
{
    public int Seconds { get; } = seconds;
}

[AttributeUsage(AttributeTargets.Class)]
public sealed class RequirePermissionAttribute(string permission) : Attribute
{
    public string Permission { get; } = permission;
}

/// <summary>
/// Gates a request on a <b>feature entitlement</b>: the handler runs only if
/// <paramref name="feature"/> is available to the current tenant (blueprint §5.11, §14).
/// Evaluated <b>before</b> the permission check — a feature disabled by entitlement is
/// unavailable to everyone in the tenant, including its admins, so there is no point
/// asking whether the user is permitted. A no-op until feature management is wired
/// (<c>AddFeatureGate</c>); fail-closed thereafter.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class RequireFeatureAttribute(string feature) : Attribute
{
    public string Feature { get; } = feature;
}
