namespace Modulus.Identity.Abstractions;

/// <summary>
/// Whether an account may start new access (sign-in, token refresh, passkeys, shop-floor PINs, mail). Both the lifecycle and the legacy
/// <c>IsActive</c> flag must allow it. The flag can only restrict: a writer that bypasses <see cref="ModulusUser"/>'s manager and clears
/// the flag still stops the account, and a flag can never reactivate one. Remove the flag term when <c>IsActive</c> is retired.
/// </summary>
internal static class AccountAccess
{
    public static bool MayStartNewAccess(this ModulusUser user)
        => user.IsActive && AccountLifecycleRules.AllowsNewAccess(user.Lifecycle);
}
