using ModulusSample.Modules.Identity.Domain.Events;
using ModulusSample.Shared.Domain;
using ModulusSample.Shared.Domain.ValueObjects;
using System.Security.Cryptography;
using System.Text;
using ModulusSample.Modules.Identity.Domain.ValueObjects;

namespace ModulusSample.Modules.Identity.Domain.Entities;

/// <summary>
/// Represents an email verification token with SHA-256 hashing and expiry.
/// </summary>
public sealed class EmailVerificationToken
{
    private const int TokenSizeBytes = 32; // 256 bits
    private const int HashOutputBytes = 32; // SHA-256 outputs 256 bits (32 bytes)

    private EmailVerificationToken() { }

    private EmailVerificationToken(Guid id, UserId userId, string tokenHash, DateTime expiresAtUtc)
    {
        Id = id;
        UserId = userId;
        TokenHash = tokenHash;
        ExpiresAtUtc = expiresAtUtc;
        IsUsed = false;
        CreatedAtUtc = DateTime.UtcNow;
    }

    // ===== PROPERTIES =====
    public Guid Id { get; private set; }
    public UserId UserId { get; private set; }
    public string TokenHash { get; private set; }  // SHA-256 hash
    public DateTime ExpiresAtUtc { get; private set; }
    public bool IsUsed { get; private set; }
    public DateTime? UsedAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    // ===== COMPUTED PROPERTIES =====
    public bool IsExpired => DateTime.UtcNow > ExpiresAtUtc;
    public bool IsValid => !IsUsed && !IsExpired;

    // ===== FACTORY METHODS =====

    /// <summary>
    /// Creates a new email verification token with a cryptographically secure random value.
    /// </summary>
    /// <param name="userId">The user ID this token is for.</param>
    /// <param name="expiryHours">How many hours until the token expires (default: 24 hours).</param>
    /// <returns>A tuple containing the token entity and the raw token string.</returns>
    public static (EmailVerificationToken Token, string RawToken) Create(Guid userId, int expiryHours = 24)
    {
        // Generate cryptographically secure random token
        string rawToken = GenerateSecureRandomToken();
        string tokenHash = ComputeSha256Hash(rawToken);

        EmailVerificationToken token = new(
            Guid.NewGuid(),
            new UserId(userId),
            tokenHash,
            DateTime.UtcNow.AddHours(expiryHours));

        return (token, rawToken);
    }

    // ===== BUSINESS LOGIC =====

    /// <summary>
    /// Verifies the provided raw token against the stored hash using constant-time comparison.
    /// </summary>
    /// <param name="rawToken">The raw token to verify.</param>
    /// <returns>Result indicating success or failure with appropriate error.</returns>
    public Result<bool> Verify(string rawToken)
    {
        if (IsUsed)
        {
            return Result.Failure<bool>(
                Error.Validation("EmailVerificationToken.AlreadyUsed", "Token has already been used"));
        }

        if (IsExpired)
        {
            return Result.Failure<bool>(
                Error.Validation("EmailVerificationToken.Expired", "Token has expired"));
        }

        string providedHash = ComputeSha256Hash(rawToken);

        // Use constant-time comparison to prevent timing attacks
        if (!ConstantTimeEquals(TokenHash, providedHash))
        {
            return Result.Failure<bool>(
                Error.Validation("EmailVerificationToken.Invalid", "Invalid token"));
        }

        return Result.Success(true);
    }

    /// <summary>
    /// Marks the token as used and records the timestamp.
    /// </summary>
    public void MarkAsUsed()
    {
        if (IsUsed)
        {
            return; // Already marked as used, no-op
        }

        IsUsed = true;
        UsedAtUtc = DateTime.UtcNow;

        // Note: Domain events are raised by the User aggregate root
        // This entity is part of the User aggregate
    }

    // ===== PRIVATE HELPER METHODS =====

    /// <summary>
    /// Generates a cryptographically secure random token.
    /// </summary>
    private static string GenerateSecureRandomToken()
    {
        byte[] randomBytes = new byte[TokenSizeBytes];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);

        // Convert to base64 URL-safe string
        return Convert.ToBase64String(randomBytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    /// <summary>
    /// Computes SHA-256 hash of the input string.
    /// </summary>
    private static string ComputeSha256Hash(string input)
    {
        byte[] inputBytes = Encoding.UTF8.GetBytes(input);
        byte[] hashBytes = SHA256.HashData(inputBytes);

        // Convert to hexadecimal string
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// Constant-time string comparison to prevent timing attacks.
    /// </summary>
    private static bool ConstantTimeEquals(string a, string b)
    {
        if (a.Length != b.Length)
        {
            return false;
        }

        int result = 0;
        for (int i = 0; i < a.Length; i++)
        {
            result |= a[i] ^ b[i];
        }

        return result == 0;
    }
}
