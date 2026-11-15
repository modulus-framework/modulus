using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Modulus.Core.Abstractions;
using Modulus.Messaging;

namespace Modulus.Messaging.Modulus;

/// <summary>
/// Sends the caller's company with each message: a message sent from a request in company A carries A (FR-21). In the
/// host context (no company) it carries none, and the ambient state of an outer scope never leaks into it.
/// </summary>
public sealed class ModulusTenantAmbientContext : IMessageAmbientContext
{
    public IDisposable? Enter(IServiceProvider scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (scope.GetService(typeof(ICurrentTenant)) is not ICurrentTenant current)
        {
            return null;
        }

        var tenant = current.IsHost ? null : current.TenantId?.ToString("N");
        return MessageTenant.Enter(tenant);
    }
}

/// <summary>
/// Runs a consumer in the company of its message. The company named in the header is verified against the tenant
/// store (<c>ITenantContextRestorer</c>). An unknown or inactive company parks the message at once with reason
/// <c>tenant-refused</c>; a transient failure of the store is retried like any other. A header that is not a company
/// id is refused too: a message is never handled without a verified company once it names one.
/// </summary>
public sealed class ModulusTenantConsumeScope : IConsumeScope
{
    public async ValueTask<Func<IDisposable?>?> PrepareAsync(IServiceProvider services, MessageEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(envelope);
        if (!envelope.Headers.TryGetValue(MessagingHeaders.TenantId, out var text) || text.Length == 0)
        {
            return null;
        }

        if (!Guid.TryParse(text, out var tenantId))
        {
            throw new InvalidOperationException($"Message {envelope.MessageId} names the tenant '{text}', which is not a tenant id.");
        }

        TenantInfo tenant;
        try
        {
            tenant = await services.VerifyTenantAsync(tenantId, cancellationToken).ConfigureAwait(false);
        }
        catch (TenantContextRejectedException exception)
        {
            // An unknown or inactive company does not become valid by retrying (D8): park it at once.
            throw new MessageParkException(
                "tenant-refused",
                $"Message {envelope.MessageId} names company {tenantId:N}, which is unknown or inactive.",
                exception);
        }

        // Entered by the bus in its consume frame; see IConsumeScope.
        return () => services.EnterTenant(tenant);
    }
}

public static class ModulusTenantMessagingExtensions
{
    /// <summary>
    /// Registers the company hand-over of FR-21: the ambient context for outgoing messages and the consume scope that
    /// verifies incoming ones. Call it after <c>AddModulusMessaging</c> and after the multi-tenancy registrations.
    /// </summary>
    public static IServiceCollection AddModulusTenantMessaging(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IMessageAmbientContext, ModulusTenantAmbientContext>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConsumeScope, ModulusTenantConsumeScope>());
        return services;
    }
}
