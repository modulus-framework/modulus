using Microsoft.EntityFrameworkCore;
using Modulus.Core.Abstractions;
using Modulus.EntityFrameworkCore;
using Modulus.Events;
using Meetup.Modules.Meetings.Application;
using Meetup.Modules.Meetings.Domain;
using Meetup.Modules.Meetings.Domain.Entities;

namespace Meetup.Modules.Meetings.Infrastructure;

/// <summary>The Meetings module's own DbContext.</summary>
public sealed class MeetingsDbContext(
    DbContextOptions<MeetingsDbContext> options,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser,
    DomainEventDispatcher dispatcher,
    IServiceProvider sp)
    : ModuleDbContext(options, currentTenant, currentUser, dispatcher, sp), IUnitOfWork
{
    // Naming is owned by dbsh SQL (schema + snake_case); the prefix is disabled.
    protected override string TablePrefix => string.Empty;

    public DbSet<MeetingGroup> Groups => Set<MeetingGroup>();
    public DbSet<MeetingGroupMember> GroupMembers => Set<MeetingGroupMember>();
    public DbSet<Meeting> Meetings => Set<Meeting>();
    public DbSet<MeetingAttendee> Attendees => Set<MeetingAttendee>();
    public DbSet<MeetingComment> Comments => Set<MeetingComment>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        base.OnModelCreating(mb);
        mb.HasDefaultSchema("meetings");
    }
}
