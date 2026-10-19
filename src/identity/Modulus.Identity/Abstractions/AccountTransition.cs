namespace Modulus.Identity.Abstractions;

/// <summary>
/// One recorded lifecycle change of an account (ADR-02). Append-only: rows are written and never updated or deleted, so the history
/// survives the account. Not a foreign key to the user, for the same reason: an archived account keeps its history.
/// </summary>
public sealed class ModulusAccountTransition
{
    /// <summary>Database key; increases with each change, so it orders the history.</summary>
    public long Id { get; set; }

    /// <summary>The account that moved.</summary>
    public Guid UserId { get; set; }

    /// <summary>The state before the change.</summary>
    public AccountLifecycle FromState { get; set; }

    /// <summary>The state after the change.</summary>
    public AccountLifecycle ToState { get; set; }

    /// <summary>The administrator or process that made the change, or null for the system.</summary>
    public Guid? ActorId { get; set; }

    /// <summary>The reason given for the change (a suspension reason code, or free text for a rehire review), or null.</summary>
    public string? Reason { get; set; }

    public DateTimeOffset OccurredAt { get; set; }
}
