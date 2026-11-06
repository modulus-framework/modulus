namespace Modulus.Analytics;

/// <summary>What a source is asked to read: one tenant's next page of a dataset.</summary>
/// <param name="TenantId">The tenant to read. Never <see cref="Guid.Empty"/>. The tenant is also the ambient tenant while the source runs.</param>
/// <param name="Cursor">Where the last page ended, as the source returned it, or <see langword="null"/> for the first page.</param>
/// <param name="Limit">The most records to return.</param>
public sealed record AnalyticsExtractRequest(Guid TenantId, string? Cursor, int Limit);

/// <summary>One page of extracted records.</summary>
/// <param name="Records">The records, all for the requested tenant, dataset and version.</param>
/// <param name="NextCursor">Where the next page starts, or <see langword="null"/> when the source has no more rows.</param>
public sealed record AnalyticsExtractPage(IReadOnlyList<AnalyticsRecord> Records, string? NextCursor);

/// <summary>
/// Reads one dataset version out of the operational store so it can be backfilled or re-synchronised. A source reads for a single tenant
/// at a time, through the tenant-filtered data path, and pages with a cursor so a run can stop and resume.
/// </summary>
/// <remarks>
/// A record's <see cref="AnalyticsRecord.EventId"/> must be stable across runs (derive it from the row's key, see
/// <see cref="AnalyticsRecordIds"/>): a second run then collapses onto the first instead of duplicating it. To make a backfilled row meet
/// the same fact captured live, the live mapper has to produce the same id.
/// </remarks>
public interface IAnalyticsSource
{
    /// <summary>The dataset name this source produces.</summary>
    string Dataset { get; }

    /// <summary>The dataset version this source produces.</summary>
    int Version { get; }

    /// <summary>Reads the next page for one tenant.</summary>
    /// <param name="request">The tenant, cursor and page size.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<AnalyticsExtractPage> ExtractAsync(AnalyticsExtractRequest request, CancellationToken cancellationToken);
}

/// <summary>What an incremental source is asked to read: one tenant's changes since a position.</summary>
/// <param name="TenantId">The tenant to read. Never <see cref="Guid.Empty"/>. The tenant is also the ambient tenant while the source runs.</param>
/// <param name="Cursor">Where the last page ended, or <see langword="null"/> to read from the beginning of history.</param>
/// <param name="Limit">The most records to return.</param>
/// <param name="SettledBefore">
/// Only changes made at or before this time (UTC) may be returned: newer ones may belong to transactions that have not committed yet, so reading them
/// could skip a change that commits late.
/// </param>
/// <param name="Overlap">
/// When the source reaches the end of the settled changes, the cursor it returns is moved back by this much, so the next poll re-reads that window and
/// picks up changes that committed late. Re-reading is harmless because a record's revision identifies its version.
/// </param>
public sealed record AnalyticsChangeRequest(Guid TenantId, string? Cursor, int Limit, DateTime SettledBefore, TimeSpan Overlap);

/// <summary>One page of changes.</summary>
/// <param name="Records">The changed records, each carrying a revision. All are for the requested tenant, dataset and version.</param>
/// <param name="Cursor">
/// Where to continue. While <paramref name="HasMore"/> is true it is the exact position after this page; once the source has caught up it is the
/// position moved back by the request's overlap. It is never <see langword="null"/> once a row has been read.
/// </param>
/// <param name="HasMore">Whether the source has more settled changes right now. When false the caller waits and polls again.</param>
public sealed record AnalyticsChangePage(IReadOnlyList<AnalyticsRecord> Records, string? Cursor, bool HasMore);

/// <summary>
/// A source that can also report what changed since a position, so a dataset can be kept up to date without re-reading everything.
/// Changes are found by a modification time, so a hard delete is not seen (a soft delete is, as an update); reconciliation reports the leftovers.
/// </summary>
public interface IAnalyticsIncrementalSource : IAnalyticsCountingSource
{
    /// <summary>Reads the next page of changes for one tenant.</summary>
    /// <param name="request">The tenant, position, page size and safety margins.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<AnalyticsChangePage> ExtractChangesAsync(AnalyticsChangeRequest request, CancellationToken cancellationToken);
}

/// <summary>Lists the tenants a backfill of "all tenants" covers.</summary>
public interface IAnalyticsTenantDirectory
{
    /// <summary>Returns the ids of the active tenants. Empty when multi-tenancy is not configured.</summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    Task<IReadOnlyList<Guid>> ListTenantIdsAsync(CancellationToken cancellationToken);
}

internal sealed class NullAnalyticsTenantDirectory : IAnalyticsTenantDirectory
{
    public Task<IReadOnlyList<Guid>> ListTenantIdsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Guid>>([]);
}
