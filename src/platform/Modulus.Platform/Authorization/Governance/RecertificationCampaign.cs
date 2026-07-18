namespace Modulus.Authorization.Governance;

/// <summary>Where a principal's access to a permission comes from, for review.</summary>
public enum AccessSource
{
    /// <summary>A direct capability grant (via role or user grant).</summary>
    Direct,

    /// <summary>Authority in force through an active delegation.</summary>
    Delegated,
}

/// <summary>The reviewer's decision on one access line during a recertification.</summary>
public enum RecertificationDecision
{
    /// <summary>Not yet reviewed.</summary>
    Pending,

    /// <summary>Confirmed still needed — access is attested.</summary>
    Certified,

    /// <summary>Reviewer determined the access should be removed.</summary>
    Revoked,
}

/// <summary>
/// One reviewable line in a recertification campaign: a (principal, permission) access and
/// where it comes from, plus the reviewer's decision.
/// </summary>
public sealed class RecertificationItem
{
    internal RecertificationItem(Guid userId, string permission, AccessSource source)
    {
        UserId = userId;
        Permission = permission;
        Source = source;
    }

    /// <summary>The principal whose access is under review.</summary>
    public Guid UserId { get; }

    /// <summary>The permission being reviewed.</summary>
    public string Permission { get; }

    /// <summary>Whether the access is direct or delegated.</summary>
    public AccessSource Source { get; }

    /// <summary>The reviewer's decision; <see cref="RecertificationDecision.Pending"/> until reviewed.</summary>
    public RecertificationDecision Decision { get; private set; } = RecertificationDecision.Pending;

    internal void Decide(RecertificationDecision decision) => Decision = decision;
}

/// <summary>
/// A periodic access-recertification campaign — the governance workflow that asks a
/// reviewer to confirm each user still needs their access (blueprint §5.14, §16). Built
/// from <see cref="EffectiveAccessReport"/> snapshots so every effective (direct and
/// delegated) permission becomes a reviewable line; the reviewer certifies or revokes each,
/// and the campaign is complete once nothing is left pending. The revoked lines are the
/// campaign's actionable output (the grants/delegations an admin flow then removes).
/// </summary>
public sealed class RecertificationCampaign
{
    private readonly List<RecertificationItem> _items;

    /// <summary>Opens a campaign named <paramref name="name"/> over the given access <paramref name="snapshots"/>.</summary>
    public RecertificationCampaign(string name, IEnumerable<EffectiveAccessReport> snapshots)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(snapshots);

        Name = name;
        _items = [];
        foreach (var report in snapshots)
        {
            if (report.UserId is not { } userId)
                continue;

            foreach (var permission in report.DirectPermissions)
                _items.Add(new RecertificationItem(userId, permission, AccessSource.Direct));
            foreach (var delegated in report.DelegatedPermissions)
                _items.Add(new RecertificationItem(userId, delegated.Permission, AccessSource.Delegated));
        }
    }

    /// <summary>The campaign name (e.g. the review period).</summary>
    public string Name { get; }

    /// <summary>Every access line under review.</summary>
    public IReadOnlyList<RecertificationItem> Items => _items;

    /// <summary>Lines not yet reviewed.</summary>
    public IReadOnlyCollection<RecertificationItem> Pending
        => [.. _items.Where(i => i.Decision == RecertificationDecision.Pending)];

    /// <summary>Lines the reviewer marked for removal — the campaign's actionable output.</summary>
    public IReadOnlyCollection<RecertificationItem> Revoked
        => [.. _items.Where(i => i.Decision == RecertificationDecision.Revoked)];

    /// <summary>True once no line is left pending.</summary>
    public bool IsComplete => _items.TrueForAll(i => i.Decision != RecertificationDecision.Pending);

    /// <summary>Confirms the (user, permission) access is still needed.</summary>
    public void Certify(Guid userId, string permission)
        => Decide(userId, permission, RecertificationDecision.Certified);

    /// <summary>Marks the (user, permission) access for removal.</summary>
    public void Revoke(Guid userId, string permission)
        => Decide(userId, permission, RecertificationDecision.Revoked);

    private void Decide(Guid userId, string permission, RecertificationDecision decision)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);
        foreach (var item in _items)
        {
            if (item.UserId == userId
                && string.Equals(item.Permission, permission, StringComparison.OrdinalIgnoreCase))
                item.Decide(decision);
        }
    }
}
