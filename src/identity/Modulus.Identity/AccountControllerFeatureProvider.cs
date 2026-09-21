using System.Reflection;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace Modulus.Identity;

/// <summary>
/// Registers the closed generic
/// <c>AccountController&lt;TUser&gt;</c> as a discoverable controller. MVC's
/// default <c>ControllerFeatureProvider</c> rejects generic controller types,
/// so without this provider the framework's account endpoints (password
/// reset, email confirmation, logout) are silently unreachable — every
/// <c>/account/*</c> route 404s. The provider is wired by
/// <c>AddModulusIdentity&lt;TContext, TUser, TRole&gt;</c>, which knows the
/// concrete user type, and the controller itself is activated through DI with
/// the matching <c>UserManager&lt;TUser&gt;</c>/<c>SignInManager&lt;TUser&gt;</c>.
/// </summary>
internal sealed class AccountControllerFeatureProvider(Type userTy)
    : IApplicationFeatureProvider<ControllerFeature>
{
    public void PopulateFeature(
        IEnumerable<ApplicationPart> parts, ControllerFeature feature)
    {
        var closed = typeof(AccountController<>).MakeGenericType(userTy);
        if (!feature.Controllers.Contains(closed.GetTypeInfo()))
            feature.Controllers.Add(closed.GetTypeInfo());
    }
}
