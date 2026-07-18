namespace Modulus.Core.Abstractions.Entities;

/// <summary>
/// Implement on an entity whose access depends on <b>who owns it</b> — the creator,
/// assignee, or responsible handler. Resource-based authorization reads this to answer
/// instance-level questions ("can this user edit <i>this</i> record, because they own
/// it?") that type-level permissions cannot (blueprint §5.7). Ownership is an explicit
/// attribute on the record, never inferred, so the single-item check and any bulk
/// filter agree.
/// </summary>
public interface IHasOwner
{
    /// <summary>The principal (user id) that owns this row.</summary>
    Guid OwnerId { get; }
}
