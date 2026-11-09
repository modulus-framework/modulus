namespace Modulus.Analytics.EntityFrameworkCore;

using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Tenancy.Lifecycle;

/// <summary>
/// Withholds analytics records of a company that is being offboarded or was offboarded. Consent is checked when a record is captured and
/// again before it is delivered, so a background job that writes for the company after the offboarding step passed queues nothing, and
/// anything it queued is not sent. It wraps whatever consent the app registered: the company must also have that consent.
/// </summary>
/// <remarks>
/// An offboarding operation of any state closes the company (it is never reopened), so a "yes it is offboarded" answer is kept for good. A "no"
/// is kept for <see cref="NotOffboardedFor"/> only, so an offboarding that starts is noticed within that time even on another node; this node
/// is told at once by <see cref="AnalyticsOffboardingStep"/>.
/// </remarks>
internal sealed class OffboardingAwareAnalyticsConsent(IAnalyticsConsent inner, IServiceScopeFactory scopes, TimeProvider clock) : IAnalyticsConsent
{
    internal static readonly TimeSpan NotOffboardedFor = TimeSpan.FromSeconds(10);

    private readonly ConcurrentDictionary<Guid, DateTime?> _checked = new();

    /// <summary>Marks the company as offboarded on this node, without asking the lifecycle store.</summary>
    internal void MarkOffboarded(Guid tenantId) => _checked[tenantId] = null;

    public async ValueTask<bool> IsGrantedAsync(Guid tenantId, string dataset, CancellationToken cancellationToken)
    {
        if (tenantId != Guid.Empty && await IsOffboardedAsync(tenantId, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        return await inner.IsGrantedAsync(tenantId, dataset, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<bool> IsOffboardedAsync(Guid tenantId, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        if (_checked.TryGetValue(tenantId, out var freshUntil) && (freshUntil is null || freshUntil > now))
        {
            return freshUntil is null;
        }

        await using var scope = scopes.CreateAsyncScope();
        var lifecycle = scope.ServiceProvider.GetService<ITenantLifecycleProvider>();
        var offboarded = lifecycle is not null
            && (await lifecycle.ListOperationsAsync(tenantId, ct).ConfigureAwait(false)).Any(o => o.Kind == OperationKind.Offboarding);
        _checked[tenantId] = offboarded ? null : now + NotOffboardedFor;
        return offboarded;
    }
}
