using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.Mediator.Extensions;
using ModulusSample.Modules.Sales.Application;
using Modulus.EntityFrameworkCore.Abstractions;
using ModulusSample.Modules.Sales.Domain.Repositories;
using ModulusSample.Modules.Sales.Infrastructure.Database;
using ModulusSample.Modules.Sales.Infrastructure.Repositories;
using Modulus.Outbox.Extensions;
using Modulus.Inbox.Extensions;
using Modulus.Outbox.Abstractions;
using Modulus.Inbox.Abstractions;

namespace ModulusSample.Modules.Sales.Infrastructure;

public sealed class SalesModule : ModulusModule
{
    public override void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException("Database connection string not configured");

        services.AddDbContext<SalesDbContext>(options =>
            options.UseNpgsql(connectionString));

        services.AddMediatorHandlers(typeof(AssemblyReference).Assembly);
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<SalesDbContext>());
        services.AddScoped<ISalesOrderRepository, EfSalesOrderRepository>();

        services.AddOutbox<SalesDbContext>(opts =>
        {
            var outboxConfig = configuration.GetSection("Outbox");
            opts.BatchSize = outboxConfig.GetValue<int>("BatchSize");
            opts.MaxRetries = outboxConfig.GetValue<int>("MaxRetries");
            opts.PollingIntervalSec = outboxConfig.GetValue<int>("IntervalInSeconds");
            opts.InitialBackoffSec = outboxConfig.GetValue<int>("InitialDelayInSeconds");
        });

        services.AddInbox<SalesDbContext>(opts =>
        {
            var inboxConfig = configuration.GetSection("Inbox");
            opts.MaxRetries = inboxConfig.GetValue<int>("MaxRetries");
            opts.HandlerRetryCount = inboxConfig.GetValue<int>("MaxRetries");
        });
    }
}