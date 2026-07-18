namespace Modulus.AspNetCore.Correlation;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Modulus.Core.Abstractions;
using Modulus.Core.Correlation;

/// <summary>
/// Registration + pipeline helpers for request correlation. Pair
/// <see cref="AddModulusCorrelation"/> (services) with
/// <see cref="UseModulusCorrelation"/> (middleware).
/// </summary>
public static class CorrelationExtensions
{
    /// <summary>
    /// Registers the singleton <see cref="ICorrelationContext"/> (AsyncLocal) and
    /// binds <see cref="CorrelationOptions"/> from the <c>Correlation</c> section.
    /// Registered as a singleton so the outbound propagation handler (pooled with
    /// the HTTP message handler) can depend on it.
    /// </summary>
    public static IServiceCollection AddModulusCorrelation(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<CorrelationOptions>(
            configuration.GetSection(CorrelationOptions.SectionName));
        services.TryAddSingleton<ICorrelationContext, CorrelationContext>();
        return services;
    }

    /// <summary>
    /// Adds <see cref="CorrelationIdMiddleware"/>. Place this first in the
    /// pipeline so every log line, span, and exception for the request already
    /// carries the correlation id.
    /// </summary>
    public static IApplicationBuilder UseModulusCorrelation(this IApplicationBuilder app)
        => app.UseMiddleware<CorrelationIdMiddleware>();
}
