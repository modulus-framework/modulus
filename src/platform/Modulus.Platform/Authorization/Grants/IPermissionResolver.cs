namespace Modulus.Authorization.Grants;

/// <summary>
/// Computes a principal's <b>effective permission set</b> server-side from the
/// grant store and the frozen permission catalog. This is the single place that
/// decides "does this principal hold this capability" — the Policy Decision Point
/// for the capability (RBAC) layer of the blueprint's composition model (§6/§7).
/// </summary>
/// <remarks>
/// Resolution semantics (fail-closed throughout):
/// <list type="bullet">
/// <item>An <c>Allow</c> grant makes a permission effective; a <c>Deny</c> grant
///   for the same permission removes it, regardless of source (deny-override).</item>
/// <item><b>Implication:</b> an allowed permission also confers everything it
///   <see cref="Modulus.Core.Abstractions.PermissionDefinition.Requires"/>,
///   transitively (approve implies read). Denies are applied <em>after</em> the
///   implication closure, so an explicit deny still wins.</item>
/// <item><b>Wildcards:</b> a grant of <c>module:group:*</c> expands to every
///   registered permission under that prefix; unknown wildcards expand to nothing.</item>
/// <item>No grants ⇒ no permissions.</item>
/// </list>
/// </remarks>
public interface IPermissionResolver
{
    /// <summary>
    /// Resolves the complete set of permission names the principal effectively
    /// holds. The set is case-insensitive and never null.
    /// </summary>
    IReadOnlySet<string> Resolve(PrincipalGrantQuery principal);
}
