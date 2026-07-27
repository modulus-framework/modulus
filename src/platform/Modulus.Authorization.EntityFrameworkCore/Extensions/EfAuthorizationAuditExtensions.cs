namespace Modulus.Authorization.EntityFrameworkCore.Audit;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Modulus.Authorization.Audit;
using Modulus.Events;
using Modulus.Events.Abstractions;

/// <summary>
/// Registration for durable authorization audit emission (auth blueprint
/// §5.14/§16): administrative changes made through
/// <c>Modulus.Authorization.Management</c>'s admin API are persisted as
/// outbox rows on <see cref="AuthorizationStoreDbContext"/> and relayed via the
/// host's <see cref="Modulus.Outbox.Abstractions.IOutboxDispatcher"/>.
/// </summary>
public static class EfAuthorizationAuditExtensions
{
    /// <summary>
    /// Supersedes the no-op <see cref="NullAuthorizationAuditWriter"/> default
    /// registered by <c>AddModulusAuthorization()</c> with the durable EF-backed
    /// writer, and starts the background relay that dispatches persisted rows.
    /// Requires <see cref="EfAuthorizationStoreExtensions.AddEfCoreAuthorizationStores"/>
    /// to already be registered (this reuses its
    /// <see cref="Microsoft.EntityFrameworkCore.IDbContextFactory{TContext}"/>),
    /// and requires some <c>IOutboxDispatcher</c> to be registered for the relay
    /// to actually deliver events (see <see cref="AuthorizationAuditRelayService"/>
    /// remarks) — rows are durably persisted either way.
    /// <code>
    /// services.AddModulusAuthorization();
    /// services.AddEfCoreAuthorizationStores(o => o.UseNpgsql(connectionString));
    /// services.AddEfCoreAuthorizationAudit();
    /// </code>
    /// </summary>
    public static IServiceCollection AddEfCoreAuthorizationAudit(
        this IServiceCollection services,
        Action<AuthorizationAuditOptions>? configure = null)
    {
        services.Configure<AuthorizationAuditOptions>(o => configure?.Invoke(o));

        // A host that never called Modulus.Events' AddModulusEvents(...) (e.g.
        // authorization is the only messaging-adjacent feature in use) still
        // needs a registry for EfAuthorizationAuditWriter to register the audit
        // event types into and for a dispatcher to resolve them back. TryAdd so
        // an existing AddModulusEvents registration is reused, not replaced.
        services.TryAddSingleton<IIntegrationEventRegistry, IntegrationEventRegistry>();

        services.RemoveAll<IAuthorizationAuditWriter>();
        services.AddScoped<IAuthorizationAuditWriter, EfAuthorizationAuditWriter>();

        services.AddScoped<AuthorizationAuditRelayProcessor>();
        services.AddHostedService<AuthorizationAuditRelayService>();

        return services;
    }
}
