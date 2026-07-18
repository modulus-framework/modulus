namespace Modulus.AspNetCore.Security;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

/// <summary>
/// Writes a hardened set of HTTP security response headers (HSTS, nosniff,
/// frame/referrer/permissions policies, optional CSP). Configuration lives
/// under the <c>SecurityHeaders</c> section (see <see cref="SecurityHeadersOptions"/>).
/// </summary>
public static class SecurityHeadersExtensions
{
    /// <summary>Binds <see cref="SecurityHeadersOptions"/> from configuration.</summary>
    public static IServiceCollection AddModulusSecurityHeaders(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<SecurityHeadersOptions>? configure = null)
    {
        services.AddOptions<SecurityHeadersOptions>()
            .Bind(configuration.GetSection(SecurityHeadersOptions.SectionName))
            .ValidateOnStart();
        if (configure is not null)
            services.Configure(configure);
        return services;
    }

    /// <summary>
    /// Adds the security-headers middleware. Place early in the pipeline so the
    /// headers apply to every response, including errors.
    /// </summary>
    public static IApplicationBuilder UseModulusSecurityHeaders(this IApplicationBuilder app)
    {
        var options = app.ApplicationServices
            .GetService<IOptions<SecurityHeadersOptions>>()?.Value
            ?? new SecurityHeadersOptions();

        return app.Use(async (context, next) =>
        {
            // Register on the outgoing response before any body is written so the
            // values win over defaults added later in the pipeline.
            context.Response.OnStarting(static state =>
            {
                var (response, opts) = ((HttpResponse, SecurityHeadersOptions))state;
                Apply(response, opts);
                return Task.CompletedTask;
            }, (context.Response, options));

            await next();
        });
    }

    private static void Apply(HttpResponse response, SecurityHeadersOptions options)
    {
        var headers = response.Headers;

        if (options.ContentTypeOptions)
            headers["X-Content-Type-Options"] = "nosniff";

        if (!string.IsNullOrWhiteSpace(options.FrameOptions))
            headers["X-Frame-Options"] = options.FrameOptions;

        if (!string.IsNullOrWhiteSpace(options.ReferrerPolicy))
            headers["Referrer-Policy"] = options.ReferrerPolicy;

        if (!string.IsNullOrWhiteSpace(options.ContentSecurityPolicy))
            headers["Content-Security-Policy"] = options.ContentSecurityPolicy;

        if (!string.IsNullOrWhiteSpace(options.PermissionsPolicy))
            headers["Permissions-Policy"] = options.PermissionsPolicy;

        // HSTS is only meaningful (and only honoured) over HTTPS.
        if (options.EnableHsts && response.HttpContext.Request.IsHttps)
        {
            var value = $"max-age={options.HstsMaxAgeSeconds}";
            if (options.HstsIncludeSubDomains)
                value += "; includeSubDomains";
            headers["Strict-Transport-Security"] = value;
        }

        if (options.RemoveServerHeader)
            headers.Remove("Server");
    }
}
