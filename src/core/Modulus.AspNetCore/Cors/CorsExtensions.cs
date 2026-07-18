namespace Modulus.AspNetCore.Cors;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Configures a single named CORS policy from the <c>Cors</c> configuration
/// section (see <see cref="ModulusCorsOptions"/>). Apply it with
/// <see cref="UseModulusCors"/> between routing and authentication.
/// </summary>
public static class CorsExtensions
{
    public const string PolicyName = "ModulusCors";

    public static IServiceCollection AddModulusCors(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<ModulusCorsOptions>? configure = null)
    {
        var options = configuration
            .GetSection(ModulusCorsOptions.SectionName)
            .Get<ModulusCorsOptions>() ?? new ModulusCorsOptions();
        configure?.Invoke(options);

        services.AddCors(cors => cors.AddPolicy(PolicyName, policy => Build(policy, options)));
        return services;
    }

    /// <summary>Applies the Modulus CORS policy. Call before auth/authorization.</summary>
    public static IApplicationBuilder UseModulusCors(this IApplicationBuilder app)
        => app.UseCors(PolicyName);

    private static void Build(CorsPolicyBuilder policy, ModulusCorsOptions options)
    {
        var wildcard = options.AllowedOrigins is ["*"];
        if (wildcard)
        {
            // A wildcard origin is incompatible with credentials per the CORS spec;
            // AllowAnyOrigin + AllowCredentials throws at runtime, so we never combine them.
            policy.AllowAnyOrigin();
        }
        else if (options.AllowedOrigins.Length > 0)
        {
            policy.WithOrigins(options.AllowedOrigins);
            // Support scheme/subdomain wildcards like https://*.example.com.
            if (Array.Exists(options.AllowedOrigins, o => o.Contains('*', StringComparison.Ordinal)))
                policy.SetIsOriginAllowedToAllowWildcardSubdomains();
        }

        policy = options.AllowedMethods.Length > 0
            ? policy.WithMethods(options.AllowedMethods) : policy.AllowAnyMethod();
        policy = options.AllowedHeaders.Length > 0
            ? policy.WithHeaders(options.AllowedHeaders) : policy.AllowAnyHeader();

        if (options.ExposedHeaders.Length > 0)
            policy.WithExposedHeaders(options.ExposedHeaders);

        if (options.AllowCredentials && !wildcard)
            policy.AllowCredentials();

        if (options.PreflightMaxAgeSeconds > 0)
            policy.SetPreflightMaxAge(TimeSpan.FromSeconds(options.PreflightMaxAgeSeconds));
    }
}
