using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.Mediator.Extensions;
using ModulusSample.Modules.Partners.Application;
using Modulus.EntityFrameworkCore.Abstractions;
using ModulusSample.Modules.Partners.Domain.Repositories;
using ModulusSample.Modules.Partners.Infrastructure.Database;
using ModulusSample.Modules.Partners.Infrastructure.Repositories;
using Modulus.Outbox.Extensions;
using Modulus.Inbox.Extensions;
using Modulus.Outbox.Abstractions;
using Modulus.Inbox.Abstractions;

namespace ModulusSample.Modules.Partners.Infrastructure;

public sealed class PartnersModule : ModulusModule
{
    public override void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException("Database connection string not configured");

        services.AddDbContext<PartnersDbContext>(options =>
            options.UseNpgsql(connectionString));

        services.AddMediatorHandlers(typeof(AssemblyReference).Assembly);
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<PartnersDbContext>());
        services.AddScoped<IPartnerRepository, EfPartnerRepository>();

        services.AddOutbox<PartnersDbContext>(opts =>
        {
            var outboxConfig = configuration.GetSection("Outbox");
            opts.BatchSize = outboxConfig.GetValue<int>("BatchSize");
            opts.MaxRetries = outboxConfig.GetValue<int>("MaxRetries");
            opts.PollingIntervalSec = outboxConfig.GetValue<int>("IntervalInSeconds");
            opts.InitialBackoffSec = outboxConfig.GetValue<int>("InitialDelayInSeconds");
        });

        services.AddInbox<PartnersDbContext>(opts =>
        {
            var inboxConfig = configuration.GetSection("Inbox");
            opts.MaxRetries = inboxConfig.GetValue<int>("MaxRetries");
            opts.HandlerRetryCount = inboxConfig.GetValue<int>("MaxRetries");
        });
    }
}