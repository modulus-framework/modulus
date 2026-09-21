using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.Data.PostgreSQL;
using Modulus.EntityFrameworkCore.Extensions;
using Modulus.Events.Extensions;
using Modulus.Inbox.Extensions;
using Modulus.Mediator.Extensions;
using Modulus.Outbox.Extensions;
using Meetup.Modules.Payments.Application;
using Meetup.Modules.Payments.Domain;
using Meetup.Modules.Payments.Domain.Repositories;
using Meetup.Modules.Payments.Infrastructure.Repositories;

namespace Meetup.Modules.Payments.Infrastructure;

/// <summary>Composition root for the Payments module.</summary>
public sealed class PaymentsModule : ModulusModule
{
    public override void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // Schema is managed by dbsh (SQL under Database/). ExternallyManaged skips it at startup; apply with `modulus migrate update` or `dbsh migrate`.
        services.AddPostgreSQLDatabase<PaymentsDbContext>(
            configuration.GetConnectionString("Payments")
                ?? "Host=localhost;Port=5432;Database=meetup_payments;Username=meetup;Password=meetup");
        services.ExternallyManaged<PaymentsDbContext>();

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<PaymentsDbContext>());
        services.AddScoped<ISubscriptionRepository, SubscriptionRepository>();
        services.AddScoped<IMeetingFeePaymentRepository, MeetingFeePaymentRepository>();

        services.AddOutbox<PaymentsDbContext>();
        services.AddInbox<PaymentsDbContext>(typeof(IUnitOfWork).Assembly);
        services.AddModulusEvents(typeof(IUnitOfWork).Assembly);
        services.AddMediatorHandlers(typeof(IUnitOfWork).Assembly);
    }
}
