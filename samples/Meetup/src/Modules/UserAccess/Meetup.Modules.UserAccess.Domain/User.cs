using Modulus.Core.Abstractions.Domain;

namespace Meetup.Modules.UserAccess.Domain;

/// <summary>
/// System user (kgrzybek: User). Created when a registration arrives via
/// integration event; activated when the registration is confirmed.
/// </summary>
public sealed class User : AggregateRoot<Guid>
{
    public string Login { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private User()
    {
    }

    public static User Create(string login, string email, string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(login))
        {
            throw new ArgumentException("Login is required.", nameof(login));
        }

        return new User
        {
            Id = Guid.NewGuid(),
            Login = login.Trim(),
            Email = email.Trim(),
            PasswordHash = passwordHash,
            IsActive = false,
            CreatedAt = DateTime.UtcNow
        };
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    /// <summary>Verifies credentials (kgrzybek: AuthenticateCommand).</summary>
    public bool VerifyPassword(string passwordHash) =>
        IsActive && PasswordHash == passwordHash;
}
