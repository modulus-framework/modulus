namespace Modulus.Analytics.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;
using Modulus.Core.Abstractions;
using Modulus.Tenancy.Lifecycle;

/// <summary>
/// Stops analytics work for a company that is being offboarded, so nothing can put its data back after the analytics datasets are erased:
/// its backfill and incremental runs are cancelled (an incremental run never ends on its own) and its undelivered queue rows are parked, so
/// no worker sends them to a sink. The step then refuses to finish while a worker still holds a claim on the company's runs or rows: an
/// in-flight batch would otherwise land in the warehouse after the erase. The refusal fails the operation, and a failed operation is run
/// again from this step, so it passes as soon as the claims are released or expire.
/// </summary>
internal sealed class AnalyticsOffboardingStep(ModulusAnalyticsDbContext db, ICurrentTenant currentTenant, TimeProvider clock) : ITenantOffboardingStep
{
    /// <summary>The marker written to a parked row's error, so an operator can tell why it was never delivered.</summary>
    internal const string ParkedReason = "Held back: the company is being offboarded.";

    /// <inheritdoc />
    public string Name => "analytics";

    /// <inheritdoc />
    public async Task DisableAsync(CancellationToken ct)
    {
        var tenant = currentTenant.TenantId
            ?? throw new InvalidOperationException("The analytics offboarding step runs only inside a company's tenant context.");
        var now = clock.GetUtcNow().UtcDateTime;

        // Runs first: a run that is still reading would keep queueing rows behind the parking below. The claim is left alone; a worker in
        // the middle of a page releases it when it notices the cancellation.
        await db.AnalyticsBackfillRuns
            .Where(r => r.TenantId == tenant && (r.Status == AnalyticsBackfillStatus.Pending || r.Status == AnalyticsBackfillStatus.Running))
            .ExecuteUpdateAsync(
                s => s.SetProperty(r => r.Status, AnalyticsBackfillStatus.Cancelled)
                      .SetProperty(r => r.UpdatedAt, now)
                      .SetProperty(r => r.LastError, ParkedReason),
                ct).ConfigureAwait(false);

        // Parked, not deleted: the dataset handler deletes the rows afterwards, and until then a worker must not pick them up.
        await db.AnalyticsDeliveries
            .Where(d => d.TenantId == tenant && d.DeliveredAt == null && d.DeadLetteredAt == null)
            .ExecuteUpdateAsync(
                s => s.SetProperty(d => d.DeadLetteredAt, (DateTime?)now)
                      .SetProperty(d => d.LastError, ParkedReason),
                ct).ConfigureAwait(false);

        var runsInFlight = await db.AnalyticsBackfillRuns.AsNoTracking()
            .AnyAsync(r => r.TenantId == tenant && r.LockedBy != null && r.LockedUntil > now, ct).ConfigureAwait(false);
        var batchInFlight = await db.AnalyticsDeliveries.AsNoTracking()
            .AnyAsync(d => d.TenantId == tenant && d.LockedBy != null && d.LockedUntil > now && d.DeliveredAt == null, ct).ConfigureAwait(false);
        if (runsInFlight || batchInFlight)
        {
            throw new InvalidOperationException(
                $"An analytics worker is still handling company '{tenant}' ({(runsInFlight ? "backfill run" : "delivery batch")} in flight). "
                + "The step is repeated when the operation runs again; the analytics datasets are not erased until it passes.");
        }
    }
}
