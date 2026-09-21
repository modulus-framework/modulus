namespace Meetup.Modules.Meetings.Presentation;

/// <summary>Permission names for the Meetings module (colon-style so the
/// framework's permission-policy provider enforces them server-side).</summary>
public static class MeetingsPermissions
{
    public const string CreateMeeting = "meetings:create";
    public const string JoinMeeting = "meetings:join";
    public const string Comment = "meetings:comment";
    public const string ViewMeetings = "meetings:view";
}
