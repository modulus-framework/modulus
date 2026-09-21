using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.Data.PostgreSQL;
using Modulus.EntityFrameworkCore.Extensions;
using Modulus.Events.Extensions;
using Modulus.Inbox.Extensions;
using Modulus.Mediator.Extensions;
using Modulus.Outbox.Extensions;
using Meetup.Modules.Administration.Application;
using Meetup.Modules.Administration.Domain;

namespace Meetup.Modules.Administration.Infrastructure;

/// <summary>Composition root for the Administration module.</summary>
public sealed class AdministrationModule : ModulusModule
{
    public override void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // Schema is managed by dbsh (SQL under Database/). ExternallyManaged skips it at startup; apply with `modulus migrate update` or `dbsh migrate`.
        services.AddPostgreSQLDatabase<AdministrationDbContext>(
            configuration.GetConnectionString("Administration")
                ?? "Host=localhost;Port=5432;Database=meetup_administration;Username=meetup;Password=meetup");
        services.ExternallyManaged<AdministrationDbContext>();

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AdministrationDbContext>());
        services.AddScoped<IMeetingGroupProposalRepository, MeetingGroupProposalRepository>();

        services.AddOutbox<AdministrationDbContext>();
        services.AddInbox<AdministrationDbContext>(typeof(IUnitOfWork).Assembly);
        services.AddModulusEvents(typeof(IUnitOfWork).Assembly);
        services.AddMediatorHandlers(typeof(IUnitOfWork).Assembly);
    }
}
