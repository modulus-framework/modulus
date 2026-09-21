namespace Meetup.Shared.Presentation;

/// <summary>
/// Authentik group names used as application roles (the API maps the JWT
/// <c>groups</c> claim to roles at token validation; see Program.cs).
/// Every app user belongs to <see cref="Members"/>; organizers additionally
/// belong to <see cref="Organizers"/>, administrators to <see cref="Admins"/>.
/// </summary>
public static class MeetupRoles
{
    public const string Members = "meetup-members";
    public const string Organizers = "meetup-organizers";
    public const string Admins = "meetup-admins";
}
