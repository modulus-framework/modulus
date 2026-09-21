using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.Data.PostgreSQL;
using Modulus.EntityFrameworkCore.Extensions;
using Modulus.Events.Extensions;
using Modulus.Inbox.Extensions;
using Modulus.Mediator.Extensions;
using Modulus.Outbox.Extensions;
using Meetup.Modules.Registrations.Application;
using Meetup.Modules.Registrations.Domain;

namespace Meetup.Modules.Registrations.Infrastructure;

/// <summary>Composition root for the Registrations module.</summary>
public sealed class RegistrationsModule : ModulusModule
{
    public override void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // Schema is managed by dbsh (SQL under Database/). ExternallyManaged skips it at startup; apply with `modulus migrate update` or `dbsh migrate`.
        services.AddPostgreSQLDatabase<RegistrationsDbContext>(
            configuration.GetConnectionString("Registrations")
                ?? "Host=localhost;Port=5432;Database=meetup_registrations;Username=meetup;Password=meetup");
        services.ExternallyManaged<RegistrationsDbContext>();

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<RegistrationsDbContext>());
        services.AddScoped<IUserRegistrationRepository, UserRegistrationRepository>();

        // Transactional outbox (ModuleDbContext enqueues IIntegrationEvent
        // domain events into this context's outbox_messages table) + polling
        // relay, and idempotent inbox (InboxMessage mapped into every module
        // context, handlers wrapped with the dedup decorator).
        services.AddOutbox<RegistrationsDbContext>();
        services.AddInbox<RegistrationsDbContext>(typeof(IUnitOfWork).Assembly);

        services.AddModulusEvents(typeof(IUnitOfWork).Assembly);
        services.AddMediatorHandlers(typeof(IUnitOfWork).Assembly);
    }
}
