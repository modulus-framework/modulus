namespace Modulus.Events;

using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.Events.Abstractions;

/// <summary>
/// Restores the ambient business context carried on an
/// <see cref="IntegrationEventEnvelope"/> for the duration of handler
/// invocation: <see cref="IntegrationEventEnvelope.TenantId"/> flows into
/// <see cref="ICurrentTenant"/> (so tenant query filters resolve to the
/// originating tenant rather than falling through to host scope, where
/// filters match every tenant and writes would stamp <c>TenantId</c> empty),
/// and <see cref="IntegrationEventEnvelope.CorrelationId"/> flows into
/// <see cref="ICorrelationContext"/> (so logs and traces stay joinable with
/// the producing operation). Broker consumers (RabbitMQ, Kafka) wrap dispatch
/// in this scope; the Rebus saga path restores the same values from message
/// headers via its own incoming step.
/// </summary>
public sealed class EnvelopeAmbientScope : IDisposable
{
    private readonly IDisposable? _tenant;
    private readonly IDisposable? _correlation;

    private EnvelopeAmbientScope(IDisposable? tenant, IDisposable? correlation)
    {
        _tenant = tenant;
        _correlation = correlation;
    }

    /// <summary>
    /// Resolves <see cref="ICurrentTenant"/>/<see cref="ICorrelationContext"/>
    /// from the consumer's DI scope and enters matching scopes for the values
    /// carried on the envelope. Both are optional: absent registrations or
    /// envelope values simply produce no scope, mirroring the Rebus step.
    /// </summary>
    public static EnvelopeAmbientScope Restore(
        IntegrationEventEnvelope envelope,
        IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(services);

        var currentTenant = services.GetService<ICurrentTenant>();
        var correlationContext = services.GetService<ICorrelationContext>();

        var tenantScope =
            envelope.TenantId is { } tenantId && currentTenant is not null
                ? currentTenant.Change(new TenantInfo(tenantId, string.Empty))
                : null;

        var correlationScope =
            !string.IsNullOrEmpty(envelope.CorrelationId)
            && correlationContext is not null
                ? correlationContext.BeginScope(envelope.CorrelationId)
                : null;

        return new EnvelopeAmbientScope(tenantScope, correlationScope);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _tenant?.Dispose();
        _correlation?.Dispose();
    }
}
