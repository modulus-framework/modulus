namespace Modulus.Identity;

/// <summary>
/// The expiry rule of <c>Identity:Password:MaxAgeDays</c>, kept free of storage so it is testable on its own.
/// </summary>
internal static class PasswordExpiry
{
    /// <summary>
    /// <c>true</c> when the policy sets an age and the password was set longer ago than that. With no recorded change
    /// (<paramref name="lastChangedAt"/> is <c>null</c>) the password is not expired.
    /// </summary>
    public static bool IsExpired(DateTimeOffset? lastChangedAt, int maxAgeDays, DateTimeOffset now)
        => maxAgeDays > 0 && lastChangedAt is { } changed && now - changed > TimeSpan.FromDays(maxAgeDays);
}
