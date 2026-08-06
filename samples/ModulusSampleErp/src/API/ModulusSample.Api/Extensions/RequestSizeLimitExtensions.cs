using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;

namespace ModulusSample.Api.Extensions;

/// <summary>
/// Extension methods for configuring request size limits
/// </summary>
public static class RequestSizeLimitExtensions
{
    public static IServiceCollection AddRequestSizeLimits(this IServiceCollection services)
    {
        // SEC-008: Configure request size limits to prevent DoS attacks
        services.Configure<FormOptions>(options =>
        {
            // Maximum size for form data (10 MB)
            options.MultipartBodyLengthLimit = 10 * 1024 * 1024;

            // Maximum size for individual form fields (1 MB)
            options.ValueLengthLimit = 1 * 1024 * 1024;

            // Maximum number of form fields
            options.ValueCountLimit = 1024;
        });

        return services;
    }

    public static IApplicationBuilder UseRequestSizeLimits(this IApplicationBuilder app)
    {
        // This is configured at the Kestrel level in appsettings.json
        // or via WebApplicationBuilder configuration
        return app;
    }
}
