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
    /// Resolves <see cref="ICurrentTenant"/>, <see cref="ICorrelationContext"/>,
    /// and <see cref="ICausationIdContext"/> from the consumer's DI scope and
    /// enters matching scopes for the values carried on the envelope. All are
    /// optional: absent registrations or envelope values simply produce no scope,
    /// mirroring the Rebus step.
    /// </summary>
    public static EnvelopeAmbientScope Restore(
        IntegrationEventEnvelope envelope,
        IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(services);

        var currentTenant = services.GetService<ICurrentTenant>();
        var correlationContext = services.GetService<ICorrelationContext>();
        var causationContext = services.GetService<ICausationIdContext>();

        var tenantScope =
            envelope.TenantId is { } tenantId && currentTenant is not null
                ? currentTenant.Change(new TenantInfo(tenantId, string.Empty))
                : null;

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

    /// <inheritdoc />
    public void Dispose()
    {
        _tenant?.Dispose();
        _correlation?.Dispose();
        _causation?.Dispose();
    }
}
