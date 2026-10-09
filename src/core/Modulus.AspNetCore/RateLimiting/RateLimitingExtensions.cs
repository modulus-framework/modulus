using Modulus.AspNetCore.Configuration;

namespace Modulus.AspNetCore.RateLimiting;

using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
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
        services.AddValidatedOptions(configuration, RateLimitingOptions.SectionName, configure);

        // Bind ONCE here so the AddRateLimiter delegate and the stored
        // IOptions view cannot diverge (a side-effecting configure delegate
        // applied twice produced different option instances).
        var options = section.Get<RateLimitingOptions>() ?? new RateLimitingOptions();
        configure?.Invoke(options);

        var window = TimeSpan.FromSeconds(Math.Max(1, options.WindowSeconds));
        // A partition is evictable after being idle well past one full window,
        // and the sweeper runs frequently enough that churn doesn't pile up.
        var idleThreshold = TimeSpan.FromTicks(window.Ticks * 4);
        var sweepInterval =
            window > TimeSpan.FromSeconds(15) ? window : TimeSpan.FromSeconds(15);

        var evictor = new EvictableFixedWindowLimiter(
            context => ResolvePartitionKey(context, options.Partition),
            () => new FixedWindowRateLimiterOptions
            {
                PermitLimit = options.PermitLimit,
                Window = window,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = options.QueueLimit,
            },
            idleThreshold,
            sweepInterval);
        services.AddSingleton(evictor);

        // A much tighter, IP-keyed budget for the endpoints password guessing and token spraying go through. It runs first,
        // so rejected sign-in attempts do not also burn the caller's general allowance.
        PartitionedRateLimiter<HttpContext> global = evictor;
        if (options.SensitivePermitLimit > 0 && options.SensitivePaths.Length > 0)
        {
            var sensitiveWindow = TimeSpan.FromSeconds(Math.Max(1, options.SensitiveWindowSeconds));
            var prefixes = options.SensitivePaths
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => new PathString(p.StartsWith('/') ? p : "/" + p))
                .ToArray();
            var sensitive = new EvictableFixedWindowLimiter(
                IpKey,
                () => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = options.SensitivePermitLimit,
                    Window = sensitiveWindow,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 0,
                },
                TimeSpan.FromTicks(sensitiveWindow.Ticks * 4),
                sensitiveWindow > TimeSpan.FromSeconds(15) ? sensitiveWindow : TimeSpan.FromSeconds(15),
                context => prefixes.Any(prefix => context.Request.Path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase)));
            services.AddSingleton(sensitive);
            global = PartitionedRateLimiter.CreateChained(sensitive, evictor);
        }

        services.AddHostedService<RateLimitPartitionSweeper>();

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = options.RejectionStatusCode;
            limiter.GlobalLimiter = global;
            limiter.OnRejected = (rejected, ct) => WriteRejectionAsync(rejected, options, ct);
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

    // Tells the caller when to come back, in the form its protocol understands. A gRPC client reads the status from a
    // trailers-only response: a bare 429 would reach it as Unavailable, which its retry policy then hammers.
    private static async ValueTask WriteRejectionAsync(
        OnRejectedContext rejected, RateLimitingOptions options, CancellationToken ct)
    {
        var http = rejected.HttpContext;
        var retryAfter = rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out var wait)
            ? Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds))
            : Math.Max(1, options.WindowSeconds);
        http.Response.Headers.RetryAfter = retryAfter.ToString(System.Globalization.CultureInfo.InvariantCulture);

        if (http.Request.ContentType?.StartsWith("application/grpc", StringComparison.OrdinalIgnoreCase) == true)
        {
            http.Response.StatusCode = StatusCodes.Status200OK;
            http.Response.ContentType = "application/grpc";
            http.Response.Headers["grpc-status"] = "8"; // ResourceExhausted
            http.Response.Headers["grpc-message"] = "Rate limit exceeded";
            return;
        }

        await Http.ProblemResponses.WriteAsync(
            http, http.Response.StatusCode, "Too many requests", "RATE_LIMITED");
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
