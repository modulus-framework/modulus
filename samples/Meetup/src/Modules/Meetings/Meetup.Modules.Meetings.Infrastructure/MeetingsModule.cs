using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.Data.PostgreSQL;
using Modulus.EntityFrameworkCore.Extensions;
using Modulus.Events.Extensions;
using Modulus.Inbox.Extensions;
using Modulus.Mediator.Extensions;
using Modulus.Outbox.Extensions;
using Meetup.Modules.Meetings.Application;
using Meetup.Modules.Meetings.Domain;
using Meetup.Modules.Meetings.Domain.Repositories;
using Meetup.Modules.Meetings.Infrastructure.Repositories;

namespace Meetup.Modules.Meetings.Infrastructure;

/// <summary>Composition root for the Meetings module.</summary>
public sealed class MeetingsModule : ModulusModule
{
    public override void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // Schema is managed by dbsh (SQL under Database/). ExternallyManaged skips it at startup; apply with `modulus migrate update` or `dbsh migrate`.
        services.AddPostgreSQLDatabase<MeetingsDbContext>(
            configuration.GetConnectionString("Meetings")
                ?? "Host=localhost;Port=5432;Database=meetup_meetings;Username=meetup;Password=meetup");
        services.ExternallyManaged<MeetingsDbContext>();

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<MeetingsDbContext>());
        services.AddScoped<IMeetingGroupRepository, MeetingGroupRepository>();
        services.AddScoped<IMeetingGroupMemberRepository, MeetingGroupMemberRepository>();
        services.AddScoped<IMeetingRepository, MeetingRepository>();
        services.AddScoped<IMeetingAttendeeRepository, MeetingAttendeeRepository>();
        services.AddScoped<IMeetingCommentRepository, MeetingCommentRepository>();

        services.AddOutbox<MeetingsDbContext>();
        services.AddInbox<MeetingsDbContext>(typeof(IUnitOfWork).Assembly);
        services.AddModulusEvents(typeof(IUnitOfWork).Assembly);
        services.AddMediatorHandlers(typeof(IUnitOfWork).Assembly);
    }
}
