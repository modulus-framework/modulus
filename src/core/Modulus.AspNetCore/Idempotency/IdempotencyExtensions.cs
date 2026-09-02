using Modulus.AspNetCore.Configuration;

namespace Modulus.AspNetCore.Idempotency;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>
/// Registration + pipeline helpers for HTTP request idempotency. Pair
/// <see cref="AddModulusIdempotency"/> (services) with
/// <see cref="UseModulusIdempotency"/> (middleware).
/// </summary>
public static class IdempotencyExtensions
{
    /// <summary>
    /// Binds <see cref="IdempotencyOptions"/> from the <c>Idempotency</c> section
    /// and registers the default in-process <see cref="IIdempotencyStore"/>. To use
    /// a shared store across instances, register your own
    /// <see cref="IIdempotencyStore"/> before calling this — <c>TryAdd</c> leaves it
    /// in place.
    /// </summary>
    public static IServiceCollection AddModulusIdempotency(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IdempotencyOptions>? configure = null)
    {
        services.AddValidatedOptions(
            configuration, IdempotencyOptions.SectionName, configure);

        services.TryAddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();
        services.AddHostedService<IdempotencyStoreSweeper>();
        return services;
    }

    /// <summary>
    /// Adds <see cref="IdempotencyMiddleware"/>. Place it after correlation and
    /// authentication (so keys are scoped to the right tenant/user) but before the
    /// module pipeline that handles the request.
    /// </summary>
    public static IApplicationBuilder UseModulusIdempotency(this IApplicationBuilder app)
        => app.UseMiddleware<IdempotencyMiddleware>();
}
