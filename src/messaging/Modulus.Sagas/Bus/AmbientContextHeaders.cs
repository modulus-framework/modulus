namespace Modulus.Sagas.Bus;

using System.Globalization;
using Modulus.Core.Abstractions;

/// <summary>
/// Message-header contract for carrying the ambient business context
/// (<see cref="ICurrentTenant"/> / <see cref="ICorrelationContext"/>) on every
/// message published through the Rebus transport, plus pure stamp/read
/// helpers shared by the publishers and the incoming pipeline step.
/// </summary>
/// <remarks>
/// Mirrors what the RabbitMQ/Kafka buses do per transport: a saga handler
/// invoked through Rebus sees the same tenant and correlation id as the
/// request that published the originating event — so tenant query filters
/// stay correct and logs/traces remain joinable end-to-end.
/// </remarks>
public static class AmbientContextHeaders
{
    /// <summary>Header carrying the publishing tenant id.</summary>
    public const string TenantId = "mod-tenant-id";

    /// <summary>Header carrying the business correlation id.</summary>
    public const string CorrelationId = "mod-correlation-id";

    /// <summary>
    /// Stamps the current ambient context onto outgoing message headers.
    /// Values already set by the caller are preserved.
    /// </summary>
    public static void Stamp(
        IDictionary<string, string> headers,
        Guid? tenantId,
        string? correlationId)
    {
        if (tenantId.HasValue && !headers.ContainsKey(TenantId))
            headers[TenantId] = tenantId.Value.ToString();

        if (!string.IsNullOrEmpty(correlationId) && !headers.ContainsKey(CorrelationId))
            headers[CorrelationId] = correlationId!;
    }

    /// <summary>Reads the ambient context back out of incoming headers.</summary>
    public static (Guid? TenantId, string? CorrelationId) Read(
        IReadOnlyDictionary<string, string> headers)
    {
        Guid? tenantId = null;
        if (headers.TryGetValue(TenantId, out var rawTenant)
            && Guid.TryParse(rawTenant, CultureInfo.InvariantCulture, out var parsed))
            tenantId = parsed;

        string? correlationId = null;
        if (headers.TryGetValue(CorrelationId, out var rawCorrelation)
            && !string.IsNullOrEmpty(rawCorrelation))
            correlationId = rawCorrelation;

        return (tenantId, correlationId);
    }
}
