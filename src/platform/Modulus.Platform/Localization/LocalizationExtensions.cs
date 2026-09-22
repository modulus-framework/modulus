namespace Modulus.Localization;

using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

public static class LocalizationExtensions
{
    /// <summary>
    /// Registers the localization pipeline: options (bound from the
    /// <c>Localization</c> section when configuration is given), in-memory
    /// store (singleton) and scoped localizer. Pair with
    /// <see cref="UseModulusRequestLocalization"/> to honor Accept-Language.
    /// </summary>
    public static IServiceCollection AddModulusLocalization(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        var options = services.AddOptions<LocalizationOptions>();
        if (configuration is not null)
            options.Bind(configuration.GetSection(LocalizationOptions.SectionName));

        services.TryAddSingleton<ILocalizationStore, InMemoryLocalizationStore>();
        services.TryAddScoped<IModulusLocalizer, ModulusLocalizer>();
        return services;
    }

    /// <summary>
    /// Registers a module's <c>*UiLocalization.SeedAsync</c> to run once at
    /// host startup, against whichever <see cref="ILocalizationStore"/> the
    /// host has registered — in-memory, database, or otherwise. Each UI
    /// module's <c>AddModulusXxxUi</c> calls this instead of the old
    /// synchronous <c>IStartupFilter</c> seeding, which could only reach
    /// <see cref="InMemoryLocalizationStore"/> (plan finding H20).
    /// </summary>
    public static IServiceCollection AddLocalizationSeed(
        this IServiceCollection services,
        Func<ILocalizationStore, CancellationToken, Task> seed)
    {
        services.AddSingleton<IHostedService>(sp =>
            new LocalizationSeedHostedService(sp.GetRequiredService<ILocalizationStore>(), seed));
        return services;
    }

    /// <summary>
    /// Honors Accept-Language / culture cookies via the framework's
    /// <c>RequestLocalizationMiddleware</c>, driven by
    /// <see cref="LocalizationOptions"/>. Place early in the pipeline.
    /// </summary>
    public static IApplicationBuilder UseModulusRequestLocalization(this IApplicationBuilder app)
    {
        var options = app.ApplicationServices.GetRequiredService<IOptions<LocalizationOptions>>().Value;
        var cultures = options.SupportedCultures
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => new CultureInfo(c))
            .ToList();
        if (cultures.Count == 0)
            cultures.Add(new CultureInfo(options.DefaultCulture));

        return app.UseRequestLocalization(new RequestLocalizationOptions
        {
            DefaultRequestCulture = new RequestCulture(options.DefaultCulture),
            SupportedCultures = cultures,
            SupportedUICultures = cultures,
        });
    }
}
