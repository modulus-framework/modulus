namespace Modulus.Identity.Extensions;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Identity.Abstractions;
using OpenIddict.Abstractions;

public static class TokenExchangeExtensions
{
    /// <summary>
    /// Lets an agent exchange a signed-in person's token for one that acts for that person (<see cref="TokenExchangeGrant"/> at
    /// <c>/connect/token</c>, spec 12A). Settings: <c>Identity:TokenExchange</c>. Call after <c>AddModulusOpenIddict</c>. Off unless
    /// <c>Identity:TokenExchange:Enabled</c> is true; each agent also needs its act-for-people flag, set through
    /// <see cref="IIntegrationClientDirectory.SetMayActForPeopleAsync"/>.
    /// </summary>
    public static IServiceCollection AddModulusTokenExchange(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<TokenExchangeOptions>(configuration.GetSection("Identity:TokenExchange"));
        if (!configuration.GetValue<bool>("Identity:TokenExchange:Enabled"))
            return services;

        services.AddOpenIddict().AddServer(server => server.AllowCustomFlow(TokenExchangeGrant.GrantType));
        return services;
    }
}
