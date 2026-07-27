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
/// <para>
/// <b>How this relates to feature entitlements.</b> Modulus has two deliberate
/// feature layers with distinct owners: <i>entitlements</i>
/// (<c>AddFeatureGate</c> + plans/overrides behind
/// <see cref="Modulus.Core.Abstractions.IFeatureGate"/>) answer the
/// <b>commercial</b> question "may this tenant use the feature at all?", while
/// these config-bound flags answer the <b>operational</b> question "is the
/// feature currently rolled out (globally, by percentage, by time window)?".
/// Use either alone, or both: <c>RequireFeature</c> enforces the conjunction —
/// a feature is served only when the tenant is entitled <i>and</i> the rollout
/// flag is on.
/// </para>
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
