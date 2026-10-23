namespace Modulus.Authorization.Sharing;

/// <summary>
/// One person's temporary access to one record, granted by another person (A8-1). The share is bounded by the company it was made in,
/// ends at <see cref="ExpiresAt"/> and ends at once when <see cref="RevokedAt"/> is set. It never widens the sharer's own access: it only
/// names a record the sharer could already act on.
/// </summary>
public sealed record RecordShare(
    Guid Id,
    Guid? TenantId,
    string ResourceType,
    string ResourceId,
    Guid GranteeUserId,
    Guid SharedByUserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? RevokedAt)
{
    /// <summary>Whether the share still grants access at <paramref name="now"/>.</summary>
    public bool IsActiveAt(DateTimeOffset now) => RevokedAt is null && now < ExpiresAt;
}
