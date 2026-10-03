namespace Modulus.Events;

using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.Core.Correlation;
using Modulus.Events.Abstractions;

/// <summary>
/// Restores the ambient business context carried on an
/// <see cref="IntegrationEventEnvelope"/> for the duration of handler
/// invocation: <see cref="IntegrationEventEnvelope.TenantId"/> flows into
/// <see cref="ICurrentTenant"/> (so tenant query filters resolve to the
/// originating tenant rather than falling through to host scope, where
/// filters match every tenant and writes would stamp <c>TenantId</c> empty),
/// <see cref="IntegrationEventEnvelope.CorrelationId"/> flows into
/// <see cref="ICorrelationContext"/> (so logs and traces stay joinable with
/// the producing operation), and <see cref="IntegrationEventEnvelope.EventId"/>
/// flows into <see cref="ICausationIdContext"/> (so events published during
/// handling carry the causation chain). Broker consumers (RabbitMQ, Kafka) wrap
/// dispatch in this scope; the Rebus saga path restores the same values from
/// message headers via its own incoming step.
/// </summary>
public sealed class EnvelopeAmbientScope : IDisposable
{
    private readonly IDisposable? _tenant;
    private readonly IDisposable? _correlation;
    private readonly IDisposable? _causation;

    private EnvelopeAmbientScope(IDisposable? tenant, IDisposable? correlation, IDisposable? causation)
    {
        _tenant = tenant;
        _correlation = correlation;
        _causation = causation;
    }

    /// <summary>
    /// Verifies the tenant carried on <paramref name="envelope"/> through
    /// <see cref="ITenantContextRestorer"/> (null when the envelope has none). An unknown or
    /// deactivated tenant throws <see cref="TenantContextRejectedException"/>; consumers dead-letter
    /// the message. Pass the result to <see cref="Restore"/>.
    /// </summary>
    public static async ValueTask<TenantInfo?> VerifyTenantAsync(
        IntegrationEventEnvelope envelope,
        IServiceProvider services,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(services);

        return envelope.TenantId is { } tenantId
            ? await services.VerifyTenantAsync(tenantId, ct).ConfigureAwait(false)
            : null;
    }

    /// <summary>
    /// Restores <paramref name="verifiedTenant"/> (from <see cref="VerifyTenantAsync"/>) and the
    /// correlation and causation carried on <paramref name="envelope"/>. Synchronous on purpose: call
    /// it in the method that dispatches, since ambient values set inside an <c>async</c> method do
    /// not flow back to its caller.
    /// </summary>
    public static EnvelopeAmbientScope Restore(
        IntegrationEventEnvelope envelope,
        IServiceProvider services,
        TenantInfo? verifiedTenant)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(services);

        var correlationContext = services.GetService<ICorrelationContext>();
        var causationContext = services.GetService<ICausationIdContext>();

        var tenantScope = verifiedTenant is not null ? services.EnterTenant(verifiedTenant) : null;

        var correlationScope =
            !string.IsNullOrEmpty(envelope.CorrelationId)
            && correlationContext is not null
                ? correlationContext.BeginScope(envelope.CorrelationId)
                : null;

        var causationScope =
            causationContext is not null
                ? causationContext.BeginScope(envelope.EventId.ToString("N"))
                : null;

        return new EnvelopeAmbientScope(tenantScope, correlationScope, causationScope);
    }

    public void Dispose()
    {
        _tenant?.Dispose();
        _correlation?.Dispose();
        _causation?.Dispose();
    }
}
