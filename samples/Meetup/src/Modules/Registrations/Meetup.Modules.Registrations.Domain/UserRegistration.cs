using Modulus.Core.Abstractions.Domain;

namespace Meetup.Modules.Registrations.Domain;

/// <summary>
/// A request to become a user of the system (kgrzybek: UserRegistration).
/// Confirmation is a separate step so administrators (or e-mail verification)
/// can gate access. Mirrors the original "User Registration" event-storming flow.
/// </summary>
public sealed class UserRegistration : AggregateRoot<Guid>
{
    public string Login { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public RegistrationStatus Status { get; private set; }
    public DateTime RegisteredAt { get; private set; }
    public DateTime? ConfirmedAt { get; private set; }

    private UserRegistration()
    {
    }

    public static UserRegistration RegisterNewUser(
        string login,
        string email,
        string passwordHash,
        string firstName,
        string lastName)
    {
        if (string.IsNullOrWhiteSpace(login))
        {
            throw new ArgumentException("Login is required.", nameof(login));
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException("Email is required.", nameof(email));
        }

        return new UserRegistration
        {
            Id = Guid.NewGuid(),
            Login = login.Trim(),
            Email = email.Trim(),
            PasswordHash = passwordHash,
            FirstName = firstName,
            LastName = lastName,
            Status = RegistrationStatus.Pending,
            RegisteredAt = DateTime.UtcNow
        };
    }

    public void Confirm()
    {
        if (Status is RegistrationStatus.Confirmed)
        {
            return;
        }

        if (Status is RegistrationStatus.Rejected)
        {
            throw new InvalidOperationException("A rejected registration cannot be confirmed.");
        }

        Status = RegistrationStatus.Confirmed;
        ConfirmedAt = DateTime.UtcNow;
    }

    public void Reject()
    {
        if (Status is not RegistrationStatus.Pending)
        {
            throw new InvalidOperationException("Only a pending registration can be rejected.");
        }

        Status = RegistrationStatus.Rejected;
    }

    /// <summary>
    /// Attaches a domain event so <c>ModuleDbContext</c> enqueues it into the
    /// module outbox transactionally. Called by the Application handler with
    /// the module's integration event (which implements <c>IDomainEvent</c>).
    /// </summary>
    public void AddIntegrationEvent(IDomainEvent @event) => AddDomainEvent(@event);
}
