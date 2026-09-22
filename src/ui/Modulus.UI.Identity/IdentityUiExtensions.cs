namespace Modulus.UI.Identity;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Localization;

public static class IdentityUiExtensions
{
    /// <summary>
    /// Registers the Identity UI: Razor Pages services, the
    /// <see cref="IdentityUiModule"/> navigation sidecar, options bound from
    /// the <c>IdentityUi</c> section, and startup seeding of the
    /// <c>Modulus.Identity</c> localizer resource. The host must still call
    /// <c>MapRazorPages()</c> (or <see cref="MapModulusIdentityUi"/>) so the
    /// RCL pages get endpoints.
    /// <para>
    /// The pages target <c>ModulusUser</c> directly (Razor Pages cannot be
    /// generic over the app's <c>TUser</c>). Apps with a custom user type
    /// override individual pages by placing same-route <c>.cshtml</c> files in
    /// the host — host pages win over RCL pages by convention.
    /// </para>
    /// </summary>
    public static IServiceCollection AddModulusIdentityUi(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        var options = services.AddOptions<IdentityUiOptions>();
        if (configuration is not null)
            options.Bind(configuration.GetSection(IdentityUiOptions.SectionName));

        services.AddRazorPages();
        services.AddUiModule<IdentityUiModule>();
        services.AddLocalizationSeed(IdentityUiLocalization.SeedAsync);
        return services;
    }

    /// <summary>Maps Razor Pages endpoints (hosts the RCL account pages).</summary>
    public static IEndpointRouteBuilder MapModulusIdentityUi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapRazorPages();
        return endpoints;
    }
}
