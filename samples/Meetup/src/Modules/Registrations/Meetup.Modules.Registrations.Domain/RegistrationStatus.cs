namespace Meetup.Modules.Registrations.Domain;

/// <summary>Registration lifecycle: proposed → confirmed (or rejected).</summary>
public enum RegistrationStatus
{
    Pending = 0,
    Confirmed = 1,
    Rejected = 2
}
