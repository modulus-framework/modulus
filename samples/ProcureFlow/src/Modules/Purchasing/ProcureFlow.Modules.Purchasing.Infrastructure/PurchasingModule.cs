using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.Mediator.Extensions;
using ModulusSample.Modules.Purchasing.Application;
using Modulus.EntityFrameworkCore.Abstractions;
using ModulusSample.Modules.Purchasing.Domain.Repositories;
using ModulusSample.Modules.Purchasing.Infrastructure.Database;
using ModulusSample.Modules.Purchasing.Infrastructure.Repositories;
using Modulus.Outbox.Extensions;
using Modulus.Inbox.Extensions;
using Modulus.Outbox.Abstractions;
using Modulus.Inbox.Abstractions;

namespace ModulusSample.Modules.Purchasing.Infrastructure;

public sealed class PurchasingModule : ModulusModule
{
    public override void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException("Database connection string not configured");

        services.AddDbContext<PurchasingDbContext>(options =>
            options
                .UseNpgsql(
                    connectionString,
                    npgsqlOptions => npgsqlOptions
                        .MigrationsHistoryTable(
                            HistoryRepository.DefaultTableName,
                            PurchasingDbContext.SchemaName)
                        .UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
                .UseSnakeCaseNamingConvention());

        services.AddMediatorHandlers(typeof(AssemblyReference).Assembly);
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<PurchasingDbContext>());
        services.AddScoped<IRequisitionRepository, EfRequisitionRepository>();
        services.AddScoped<IPurchaseOrderRepository, EfPurchaseOrderRepository>();
        services.AddScoped<IGoodsReceiptRepository, EfGoodsReceiptRepository>();

        services.AddOutbox<PurchasingDbContext>(opts =>
        {
            var outboxConfig = configuration.GetSection("Outbox");
            opts.BatchSize = outboxConfig.GetValue<int>("BatchSize");
            opts.MaxRetries = outboxConfig.GetValue<int>("MaxRetries");
            opts.PollingIntervalSec = outboxConfig.GetValue<int>("IntervalInSeconds");
            opts.InitialBackoffSec = outboxConfig.GetValue<int>("InitialDelayInSeconds");
        });

        services.AddInbox<PurchasingDbContext>(opts =>
        {
            var inboxConfig = configuration.GetSection("Inbox");
            opts.MaxRetries = inboxConfig.GetValue<int>("MaxRetries");
            opts.HandlerRetryCount = inboxConfig.GetValue<int>("MaxRetries");
        });
    }
}