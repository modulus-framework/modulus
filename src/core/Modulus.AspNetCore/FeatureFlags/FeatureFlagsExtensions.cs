namespace Modulus.AspNetCore.FeatureFlags;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FeatureManagement;
using Microsoft.FeatureManagement.FeatureFilters;

/// <summary>
/// Registration helpers for runtime feature flags. Thin wrapper over
/// <c>Microsoft.FeatureManagement</c> so the framework owns the entry point,
/// the config section, and the default filter set. Pair
/// <see cref="AddModulusFeatureFlags"/> (services) with
/// <see cref="RequireFeatureExtensions.RequireFeature{TBuilder}"/> to gate
/// minimal-API endpoints.
/// </summary>
public static class FeatureFlagsExtensions
{
    /// <summary>
    /// Configuration section feature flags bind from — <c>FeatureManagement</c>, the
    /// library's own convention, kept so operators can follow the standard docs.
    /// </summary>
    public const string SectionName = "FeatureManagement";

    /// <summary>
    /// Binds feature flags from the <c>FeatureManagement</c> section and registers
    /// the built-in <see cref="PercentageFilter"/> and
    /// <see cref="TimeWindowFilter"/>. Evaluation is <b>scoped</b> (per request) so
    /// filters can read the ambient tenant/user. Exposes <see cref="IFeatureManager"/>
    /// and <see cref="IVariantFeatureManager"/> for injection.
    /// </summary>
    public static IServiceCollection AddModulusFeatureFlags(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScopedFeatureManagement(configuration.GetSection(SectionName))
            .AddFeatureFilter<PercentageFilter>()
            .AddFeatureFilter<TimeWindowFilter>();
        return services;
    }
}
