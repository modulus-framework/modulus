namespace Modulus.Identity.Extensions;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Modulus.Identity;
using Modulus.Identity.Abstractions;
using OpenIddict.Abstractions;

public static class ShopFloorExtensions
{
    /// <summary>
    /// Lets operators sign in at a registered line tablet with an employee code and PIN (the grant
    /// <see cref="ShopFloorGrant.GrantType"/> at <c>/connect/token</c>). Settings: <c>Identity:ShopFloor</c>. Call after
    /// <c>AddModulusIdentity</c> and <c>AddModulusOpenIddict</c>. Off unless <c>Identity:ShopFloor:Enabled</c> is true.
    /// <para>
    /// <b>Devices.</b> A tablet is an OpenIddict client bound to one company with <see cref="IIntegrationClientDirectory"/>
    /// (<c>BindAsync</c>), and it needs the grant type permission
    /// (<c>OpenIddictApplicationDescriptor.AddGrantTypePermissions(ShopFloorGrant.GrantType)</c>). An unregistered, unbound or
    /// disabled device is refused, and an operator of another company cannot sign in on it. Disabling the device
    /// (<c>DisableAsync</c>) stops new sign-ins at once.
    /// </para>
    /// <para>
    /// Tokens are short-lived, carry no refresh token, and carry only the roles in <c>AllowedRoles</c>.
    /// </para>
    /// </summary>
    public static IServiceCollection AddModulusShopFloor<TUser, TRole>(
        this IServiceCollection services,
        IConfiguration configuration)
        where TUser : ModulusUser, new()
        where TRole : ModulusRole
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<ShopFloorOptions>()
            .Bind(configuration.GetSection("Identity:ShopFloor"))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<ShopFloorOptions>, ShopFloorOptionsValidator>());

        if (!configuration.GetValue<bool>("Identity:ShopFloor:Enabled"))
            return services;

        services.AddOpenIddict().AddServer(server => server.AllowCustomFlow(ShopFloorGrant.GrantType));
        services.TryAddScoped<IShopFloorCredentialValidator, ShopFloorCredentialValidator<TUser>>();
        services.TryAddScoped<IShopFloorAccountService, ShopFloorAccountService<TUser, TRole>>();

        return services;
    }

    private sealed class ShopFloorOptionsValidator : IValidateOptions<ShopFloorOptions>
    {
        public ValidateOptionsResult Validate(string? name, ShopFloorOptions options)
            => options.IsValid(out var problem)
                ? ValidateOptionsResult.Success
                : ValidateOptionsResult.Fail(problem ?? "Identity:ShopFloor is misconfigured.");
    }
}
