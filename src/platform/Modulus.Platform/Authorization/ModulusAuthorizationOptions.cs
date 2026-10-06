namespace Modulus.Authorization;

using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

/// <summary>Options of <c>AddModulusAuthorization</c>.</summary>
public sealed class ModulusAuthorizationOptions
{
    /// <summary>
    /// Sets the fallback authorization policy to "a signed-in user" (unless the app set its own), so
    /// endpoints without authorization data are closed. Default <see langword="true"/>.
    /// </summary>
    public bool RequireAuthenticatedUserByDefault { get; set; } = true;

    /// <summary>
    /// Honours <c>permission</c> claims in the signed-in principal as a grant (a store-level deny still wins over them).
    /// Default <see langword="true"/> for hosts whose token issuer or seeder mints fine-grained claims. Set
    /// <see langword="false"/> to make the grant store the only source of permissions, so a claim in a token can never
    /// confer access the store does not.
    /// </summary>
    public bool TrustPermissionClaims { get; set; } = true;
}

/// <summary>Applies <see cref="ModulusAuthorizationOptions.RequireAuthenticatedUserByDefault"/>.</summary>
internal sealed class FallbackPolicySetup(IOptions<ModulusAuthorizationOptions> options)
    : IConfigureOptions<AuthorizationOptions>
{
    public void Configure(AuthorizationOptions authorization)
    {
        if (options.Value.RequireAuthenticatedUserByDefault)
            authorization.FallbackPolicy ??= new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
    }
}
