using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Exceptions;

namespace Modulus.Authorization.Sharing;

/// <summary>
/// Creates, checks and revokes record shares (A8-1). A share is made and checked in the caller's company only; a share from another company
/// is never found. Only the person who made a share may revoke it.
/// </summary>
public sealed class RecordShares(
    IRecordShareStore store,
    ICurrentTenant tenant,
    TimeProvider clock,
    IOptions<RecordShareOptions> options)
{
    /// <summary>The one action a share grants (<see cref="RecordShareOptions.SharedAction"/>).</summary>
    public string SharedAction => options.Value.SharedAction;

    /// <summary>
    /// The record a share names: its type name and its <c>Id</c> as text. The decision path and the HTTP endpoints both use it, so a
    /// share is always checked against the record it was made for. False when the record has no <c>Id</c>.
    /// </summary>
    public static bool TryKeyOf(object resource, out string resourceType, out string resourceId)
    {
        ArgumentNullException.ThrowIfNull(resource);
        resourceType = resource.GetType().Name;
        var id = resource.GetType().GetProperty("Id")?.GetValue(resource);
        resourceId = id?.ToString() ?? string.Empty;
        return id is not null;
    }

    /// <summary>Every share of one record in the caller's company, live or not.</summary>
    public Task<IReadOnlyList<RecordShare>> ForRecordAsync(string resourceType, string resourceId, CancellationToken ct = default)
        => store.ForRecordAsync(tenant.TenantId, resourceType, resourceId, ct);

    public async Task<RecordShare> CreateAsync(
        string resourceType, string resourceId, Guid granteeUserId, Guid sharedByUserId, TimeSpan duration, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceType);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        if (granteeUserId == sharedByUserId)
            throw new ArgumentException("A record cannot be shared with the person sharing it.", nameof(granteeUserId));
        if (duration <= TimeSpan.Zero || duration > options.Value.MaxDuration)
            throw new ArgumentOutOfRangeException(nameof(duration), duration, $"A share lasts up to {options.Value.MaxDuration}.");

        var now = clock.GetUtcNow();
        var share = new RecordShare(
            Modulus.GuidV7.Create(now), tenant.TenantId, resourceType, resourceId,
            granteeUserId, sharedByUserId, now, now + duration, RevokedAt: null);
        await store.AddAsync(share, ct).ConfigureAwait(false);
        return share;
    }

    /// <summary>Whether the person holds a live share of this record in the caller's company, now.</summary>
    public async Task<bool> IsSharedWithAsync(string resourceType, string resourceId, Guid granteeUserId, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var shares = await store.ForRecordAsync(tenant.TenantId, resourceType, resourceId, ct).ConfigureAwait(false);
        return shares.Any(s => s.GranteeUserId == granteeUserId && s.IsActiveAt(now));
    }

    /// <summary>Ends a share now. Returns false when it was already revoked or ran out; throws when the caller did not make it.</summary>
    public async Task<bool> RevokeAsync(Guid shareId, Guid revokedByUserId, CancellationToken ct = default)
    {
        var share = await store.FindAsync(shareId, ct).ConfigureAwait(false);
        if (share is null || share.TenantId != tenant.TenantId)
            return false;
        if (share.SharedByUserId != revokedByUserId)
            throw new ForbiddenException("record:share:revoke", "Only the person who shared a record can revoke the share.");
        if (share.RevokedAt is not null)
            return false;

        await store.UpdateAsync(share with { RevokedAt = clock.GetUtcNow() }, ct).ConfigureAwait(false);
        return true;
    }
}
