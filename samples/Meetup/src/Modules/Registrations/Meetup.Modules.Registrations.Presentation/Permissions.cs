namespace Meetup.Modules.Registrations.Presentation;

/// <summary>Permission names for the Registrations module (colon-style so the
/// framework's permission-policy provider enforces them server-side).</summary>
public static class RegistrationsPermissions
{
    public const string RegisterUser = "registrations:register";
    public const string ConfirmRegistration = "registrations:confirm";
    public const string ViewRegistrations = "registrations:view";
}
