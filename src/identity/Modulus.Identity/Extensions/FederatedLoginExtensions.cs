namespace Modulus.Identity.Extensions;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Modulus.Identity;
using Modulus.Identity.Abstractions;
using OpenIddict.Abstractions;

public static class FederatedLoginExtensions
{
    /// <summary>
    /// Lets clients exchange a token from an external identity provider for Modulus tokens (the grant
    /// <see cref="FederatedLoginGrant.GrantType"/> at <c>/connect/token</c>). Settings: <c>Identity:FederatedLogin</c>.
    /// <para>
    /// The external token is validated against the provider's discovery document (issuer, signing keys, lifetime, and the
    /// configured audiences). The subject links it to a local account; mapped groups become roles. The provider's
    /// tokens are not stored: only the link is kept. Call after <c>AddModulusIdentity</c> and <c>AddModulusOpenIddict</c>.
    /// </para>
    /// <para>
    /// Off unless <c>Identity:FederatedLogin:Enabled</c> is true. Then the grant is allowed on the server, and the
    /// settings are checked at startup. Each client that may exchange tokens needs the grant type permission on its
    /// OpenIddict application (<c>OpenIddictApplicationDescriptor.AddGrantTypePermissions(FederatedLoginGrant.GrantType)</c>);
    /// the seeded first-party clients do not get it by default.
    /// </para>
    /// </summary>
    public static IServiceCollection AddModulusFederatedLogin<TUser, TRole>(
        this IServiceCollection services,
        IConfiguration configuration)
        where TUser : ModulusUser, new()
        where TRole : ModulusRole
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<FederatedLoginOptions>()
            .Bind(configuration.GetSection("Identity:FederatedLogin"))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<FederatedLoginOptions>, FederatedLoginOptionsValidator>());

        if (!configuration.GetValue<bool>("Identity:FederatedLogin:Enabled"))
            return services;

        services.AddOpenIddict().AddServer(server => server.AllowCustomFlow(FederatedLoginGrant.GrantType));
        services.TryAddSingleton<IFederatedTokenReader>(sp =>
            new OidcFederatedTokenReader(sp.GetRequiredService<IOptions<FederatedLoginOptions>>().Value));
        services.Replace(ServiceDescriptor.Scoped<IFederatedLoginValidator, FederatedLoginValidator<TUser, TRole>>());

        return services;
    }

    /// <summary>Rejects an enabled <c>Identity:FederatedLogin</c> section that cannot work, with the reason, at startup.</summary>
    private sealed class FederatedLoginOptionsValidator : IValidateOptions<FederatedLoginOptions>
    {
        public ValidateOptionsResult Validate(string? name, FederatedLoginOptions options)
            => options.IsValid(out var problem)
                ? ValidateOptionsResult.Success
                : ValidateOptionsResult.Fail(problem ?? "Identity:FederatedLogin is misconfigured.");
    }
}
