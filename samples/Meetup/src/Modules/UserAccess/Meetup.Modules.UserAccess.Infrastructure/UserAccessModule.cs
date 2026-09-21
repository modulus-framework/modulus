using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.Data.PostgreSQL;
using Modulus.EntityFrameworkCore.Extensions;
using Modulus.Events.Extensions;
using Modulus.Inbox.Extensions;
using Modulus.Mediator.Extensions;
using Modulus.Outbox.Extensions;
using Meetup.Modules.UserAccess.Application;
using Meetup.Modules.UserAccess.Domain;

namespace Meetup.Modules.UserAccess.Infrastructure;

/// <summary>Composition root for the UserAccess module.</summary>
public sealed class UserAccessModule : ModulusModule
{
    public override void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // Schema is managed by dbsh (SQL under Database/). ExternallyManaged skips it at startup; apply with `modulus migrate update` or `dbsh migrate`.
        services.AddPostgreSQLDatabase<UserAccessDbContext>(
            configuration.GetConnectionString("UserAccess")
                ?? "Host=localhost;Port=5432;Database=meetup_useraccess;Username=meetup;Password=meetup");
        services.ExternallyManaged<UserAccessDbContext>();

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<UserAccessDbContext>());
        services.AddScoped<IUserRepository, UserRepository>();

        services.AddOutbox<UserAccessDbContext>();
        services.AddInbox<UserAccessDbContext>(typeof(IUnitOfWork).Assembly);
        services.AddModulusEvents(typeof(IUnitOfWork).Assembly);
        services.AddMediatorHandlers(typeof(IUnitOfWork).Assembly);
    }
}
