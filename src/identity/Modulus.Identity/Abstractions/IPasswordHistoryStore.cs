namespace Modulus.Identity.Abstractions;

/// <summary>
/// Reads an account's password history. Rows are written when the account is saved (see the identity DbContext), so
/// nothing here changes data. Internal: an implementation detail of the reuse check and expiry.
/// </summary>
internal interface IPasswordHistoryStore
{
    /// <summary>The account's most recent password hashes, newest first.</summary>
    Task<IReadOnlyList<string>> GetRecentHashesAsync(Guid userId, int count, CancellationToken ct);

    /// <summary>When the account's current password was set, or <c>null</c> when no change was ever recorded.</summary>
    Task<DateTimeOffset?> GetLastChangedAsync(Guid userId, CancellationToken ct);
}
