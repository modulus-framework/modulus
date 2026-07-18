namespace Modulus.AspNetCore.RateLimiting;

using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;

/// <summary>
/// Wires the built-in <c>System.Threading.RateLimiting</c> middleware with a
/// fixed-window limiter partitioned per user / tenant / IP. Configuration lives
/// under the <c>RateLimiting</c> section (see <see cref="RateLimitingOptions"/>).
/// </summary>
public static class RateLimitingExtensions
{
    /// <summary>
    /// Registers the Modulus rate limiter. Bind options from
    /// <paramref name="configuration"/> (<c>RateLimiting</c> section) and/or
    /// override them via <paramref name="configure"/>.
    /// </summary>
    public static IServiceCollection AddModulusRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<RateLimitingOptions>? configure = null)
    {
        var section = configuration.GetSection(RateLimitingOptions.SectionName);
        services.AddOptions<RateLimitingOptions>()
            .Bind(section)
            .ValidateOnStart();
        if (configure is not null)
            services.Configure(configure);

        services.AddRateLimiter(limiter =>
        {
            var options = section.Get<RateLimitingOptions>() ?? new RateLimitingOptions();
            if (configure is not null)
                configure(options);

            limiter.RejectionStatusCode = options.RejectionStatusCode;
            limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
                context => RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: ResolvePartitionKey(context, options.Partition),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = options.PermitLimit,
                        Window = TimeSpan.FromSeconds(options.WindowSeconds),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = options.QueueLimit,
                    }));
        });

        return services;
    }

    /// <summary>
    /// Adds the rate-limiting middleware to the pipeline when it is enabled.
    /// Place after <c>UseAuthentication</c> so per-user partitioning can see the
    /// authenticated principal, and after the tenant-resolution middleware.
    /// </summary>
    public static IApplicationBuilder UseModulusRateLimiting(this WebApplication app)
    {
        var options = app.Services
            .GetRequiredService<IOptions<RateLimitingOptions>>().Value;
        return options.Enabled ? app.UseRateLimiter() : app;
    }

    private static string ResolvePartitionKey(HttpContext context, RateLimitPartitionStrategy partition)
        => partition switch
        {
            RateLimitPartitionStrategy.Global => "global",
            RateLimitPartitionStrategy.IpAddress => IpKey(context),
            RateLimitPartitionStrategy.User => UserKey(context),
            RateLimitPartitionStrategy.Tenant => TenantKey(context),
            _ => "global",
        };

    private static string IpKey(HttpContext context)
    {
        var ip = context.Connection.RemoteIpAddress?.ToString();
        return string.IsNullOrEmpty(ip) ? "unknown-ip" : $"ip:{ip}";
    }

    private static string UserKey(HttpContext context)
    {
        // Prefer the framework's ICurrentUser (scoped) so identity semantics stay
        // consistent; fall back to the raw claim, then to per-IP for anonymous.
        var currentUser = context.RequestServices.GetService<ICurrentUser>();
        if (currentUser?.IsAuthenticated == true && currentUser.UserId is { } id)
            return $"user:{id}";

        var claim = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return string.IsNullOrEmpty(claim) ? IpKey(context) : $"user:{claim}";
    }

    private static string TenantKey(HttpContext context)
    {
        var tenant = context.RequestServices.GetService<ICurrentTenant>();
        return tenant?.TenantId is { } tenantId
            ? $"tenant:{tenantId}"
            : IpKey(context);
    }
}
