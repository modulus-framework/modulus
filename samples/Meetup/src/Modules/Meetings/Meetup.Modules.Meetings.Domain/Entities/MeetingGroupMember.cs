using Meetup.Modules.Meetings.Domain.Enums;
using Modulus.Core.Abstractions.Domain;

namespace Meetup.Modules.Meetings.Domain.Entities;

/// <summary>Membership of a login in a meeting group.</summary>
public sealed class MeetingGroupMember : AggregateRoot<Guid>
{
    public Guid GroupId { get; private set; }
    public string Login { get; private set; } = string.Empty;
    public GroupMemberRole Role { get; private set; }
    public DateTime JoinedAt { get; private set; }

    private MeetingGroupMember()
    {
    }

    public static MeetingGroupMember Join(Guid groupId, string login, GroupMemberRole role = GroupMemberRole.Member)
    {
        if (string.IsNullOrWhiteSpace(login))
        {
            throw new ArgumentException("Login is required.", nameof(login));
        }

        return new MeetingGroupMember
        {
            Id = Guid.NewGuid(),
            GroupId = groupId,
            Login = login.Trim(),
            Role = role,
            JoinedAt = DateTime.UtcNow
        };
    }

    public void PromoteToOrganizer() => Role = GroupMemberRole.Organizer;
}
