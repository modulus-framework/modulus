namespace Modulus.AuditLogging;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

public static class AuditLoggingExtensions
{
    /// <summary>
    /// Registers the audit-log pipeline: bounded in-memory store (singleton)
    /// plus ambient-context logger (scoped). Replace
    /// <see cref="IAuditLogStore"/> before this call for durable storage.
    /// </summary>
    public static IServiceCollection AddModulusAuditLogging(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IAuditLogStore, InMemoryAuditLogStore>();
        services.TryAddScoped<IAuditLogger, AuditLogger>();
        return services;
    }
}
