namespace Modulus.Identity.Abstractions;

/// <summary>
/// One password hash an account has used, with when it was set. Kept for the reuse check
/// (<c>Identity:Password:HistoryCount</c>) and for expiry (<c>Identity:Password:MaxAgeDays</c>): the newest row's
/// <see cref="ChangedAt"/> is the date the current password was set. Rows are removed with their account.
/// </summary>
public sealed class ModulusPasswordHistoryEntry
{
    /// <summary>Database key; increases with each recorded password, so it orders the history.</summary>
    public long Id { get; set; }

    public Guid UserId { get; set; }

    /// <summary>The Identity password hash, never the password.</summary>
    public string PasswordHash { get; set; } = string.Empty;

    public DateTimeOffset ChangedAt { get; set; }
}
