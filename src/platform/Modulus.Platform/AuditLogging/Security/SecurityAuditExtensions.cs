namespace Modulus.AuditLogging.Security;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Modulus.Authorization.Audit;
using Modulus.Core.Abstractions;
using Modulus.Events.Abstractions;

public static class SecurityAuditExtensions
{
    /// <summary>
    /// Turns the security audit on: replaces the no-op <see cref="ISecurityAuditLog"/> with the queued
    /// <see cref="SecurityAuditLog"/>, runs its writer, keeps chains in <see cref="ISecurityAuditStore"/> (in memory
    /// unless a durable store is registered, e.g. <c>AddModulusAuditStore&lt;TContext&gt;()</c>), anchors the heads
    /// (<see cref="SecurityAuditOptions.AnchorFile"/> or your own <see cref="IAuditAnchorSink"/>), and records the
    /// authorization audit events (grants, org changes, audited decisions) relayed by
    /// <c>AddEfCoreAuthorizationAudit</c>. Settings: <c>Security:Audit</c>.
    /// </summary>
    public static IServiceCollection AddModulusSecurityAudit(
        this IServiceCollection services, IConfiguration? configuration = null, Action<SecurityAuditOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = services.AddOptions<SecurityAuditOptions>();
        if (configuration is not null)
            options.Bind(configuration.GetSection(SecurityAuditOptions.SectionName));
        if (configure is not null)
            options.Configure(configure);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ISecurityAuditStore, InMemorySecurityAuditStore>();
        services.TryAddSingleton<SecurityAuditLog>();
        services.RemoveAll<ISecurityAuditLog>();
        services.AddSingleton<ISecurityAuditLog>(sp => sp.GetRequiredService<SecurityAuditLog>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, SecurityAuditWriter>());

        services.TryAddSingleton<AuditAnchorService>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, AuditAnchorService>(
            sp => sp.GetRequiredService<AuditAnchorService>()));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAuditAnchorSink, ConfiguredFileAnchorSink>());

        services.TryAddEnumerable(ServiceDescriptor.Scoped<
            IIntegrationEventHandler<AuthorizationAdministrativeChangeEvent>, AuthorizationSecurityAuditHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<
            IIntegrationEventHandler<AccessDecisionAuditEvent>, AuthorizationSecurityAuditHandler>());
        return services;
    }
}

/// <summary>The <see cref="FileAuditAnchorSink"/> of <see cref="SecurityAuditOptions.AnchorFile"/>; nothing when unset.</summary>
internal sealed class ConfiguredFileAnchorSink(IOptions<SecurityAuditOptions> options) : IAuditAnchorSink
{
    private readonly FileAuditAnchorSink? _file = options.Value.AnchorFile is { Length: > 0 } path ? new FileAuditAnchorSink(path) : null;

    public Task WriteAsync(IReadOnlyList<SecurityAuditHead> heads, DateTimeOffset anchoredAt, CancellationToken ct = default)
        => _file?.WriteAsync(heads, anchoredAt, ct) ?? Task.CompletedTask;
}

/// <summary>Records the authorization audit events into the security audit chain of the tenant they were raised in.</summary>
public sealed class AuthorizationSecurityAuditHandler(ISecurityAuditLog log, ICurrentTenant tenant)
    : IIntegrationEventHandler<AuthorizationAdministrativeChangeEvent>, IIntegrationEventHandler<AccessDecisionAuditEvent>
{
    /// <inheritdoc />
    public Task HandleAsync(AuthorizationAdministrativeChangeEvent @event, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(@event);
        log.Record(new SecurityAuditEvent
        {
            Category = SecurityAuditCategories.Authorization,
            Action = $"{@event.Category}.{@event.Action}".ToLowerInvariant(),
            TenantId = tenant.TenantId,
            Actor = @event.ActorUserId,
            Target = @event.TargetDescription,
            OccurredAt = new DateTimeOffset(DateTime.SpecifyKind(@event.OccurredAt, DateTimeKind.Utc)),
            Details = @event.Details.ToDictionary(d => d.Key, d => (string?)d.Value),
        });
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task HandleAsync(AccessDecisionAuditEvent @event, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(@event);
        log.Record(new SecurityAuditEvent
        {
            Category = SecurityAuditCategories.Authorization,
            Action = "access-decision",
            Outcome = @event.IsAllowed ? SecurityAuditOutcomes.Success : SecurityAuditOutcomes.Denied,
            TenantId = tenant.TenantId,
            Actor = @event.ActorUserId,
            Target = $"{@event.ResourceType}:{@event.Action}",
            OccurredAt = new DateTimeOffset(DateTime.SpecifyKind(@event.OccurredAt, DateTimeKind.Utc)),
            Details = @event.Reason is null ? new Dictionary<string, string?>() : new Dictionary<string, string?> { ["reason"] = @event.Reason },
        });
        return Task.CompletedTask;
    }
}
