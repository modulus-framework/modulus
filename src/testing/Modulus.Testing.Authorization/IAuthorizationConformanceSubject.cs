using Modulus.Authorization.Grants;

namespace Modulus.Testing.Authorization;

/// <summary>
/// What the kit needs from an app to run the permission truth table against it: a store it can fill with grants, and the production
/// resolver over that store. One subject is one company; the kit checks company isolation with two.
/// </summary>
public interface IAuthorizationConformanceSubject
{
    /// <summary>The resolver over this subject's grant store, with every permission the cases name registered.</summary>
    PermissionResolver Resolver { get; }

    /// <summary>Empties the store so the next case starts from no grants.</summary>
    Task ResetAsync(CancellationToken ct = default);

    /// <summary>Adds one grant to the store.</summary>
    Task AddGrantAsync(PermissionGrant grant, CancellationToken ct = default);
}
