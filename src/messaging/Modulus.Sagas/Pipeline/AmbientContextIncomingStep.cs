namespace Modulus.Sagas.Pipeline;

using Modulus.Core.Abstractions;
using Modulus.Sagas.Bus;
using Rebus.Messages;
using Rebus.Pipeline;

/// <summary>
/// Rebus incoming-pipeline step that restores the ambient business context
/// carried on message headers (see <see cref="AmbientContextHeaders"/>) for
/// the duration of handler invocation: the tenant flows into
/// <see cref="ICurrentTenant"/> (so tenant query filters resolve correctly)
/// and the correlation id into <see cref="ICorrelationContext"/> (so logs and
/// traces stay joinable with the originating request).
/// </summary>
public sealed class AmbientContextIncomingStep(
    ICurrentTenant? currentTenant,
    ICorrelationContext? correlationContext) : IIncomingStep
{
    public Task Process(IncomingStepContext context, Func<Task> next)
    {
        var headers = context.Load<Message>()?.Headers;
        if (headers is null || (currentTenant is null && correlationContext is null))
            return next();

        var (tenantId, correlationId) = AmbientContextHeaders.Read(headers);

        using var tenantScope = tenantId.HasValue && currentTenant is not null
            ? currentTenant.Change(new TenantInfo(tenantId.Value, string.Empty))
            : null;

        using var correlationScope = !string.IsNullOrEmpty(correlationId)
                                     && correlationContext is not null
            ? correlationContext.BeginScope(correlationId)
            : null;

        return next();
    }
}
