using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Modulus.Core.Abstractions;
using Modulus.Inbox.Extensions;
using Modulus.Mediator.Extensions;
using Modulus.Outbox.Extensions;
using TradeFlow.Modules.Inventory.Application;
using TradeFlow.Modules.Inventory.Domain.Constants;
using TradeFlow.Modules.Inventory.Domain.Repositories;
using TradeFlow.Modules.Inventory.Infrastructure.Database;
using TradeFlow.Modules.Inventory.Infrastructure.Repositories;

namespace TradeFlow.Modules.Inventory.Infrastructure;

public sealed class InventoryModule : ModulusModule
{
    public override void ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddMediatorHandlers(typeof(Application.AssemblyReference).Assembly);
        services.AddValidatorsFromAssembly(Application.AssemblyReference.Assembly);
        AddInfrastructure(services, configuration);
    }

    private static void AddInfrastructure(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<InventoryDbContext>((sp, options) =>
            options
                .UseNpgsql(
                    configuration.GetConnectionString("Inventory"),
                    npgsqlOptions => npgsqlOptions
                        .MigrationsHistoryTable(
                            HistoryRepository.DefaultTableName,
                            Schemas.Inventory)
                        .UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
                .UseSnakeCaseNamingConvention()
                // ModuleDbContext's runtime model can differ from the design-time snapshot
                // (inbox/outbox model contributors, PII converters). Schema drift is gated
                // in CI via dotnet ef migrations has-pending-model-changes, so downgrade the
                // runtime migration guard to a log.
                .ConfigureWarnings(w => w.Log(RelationalEventId.PendingModelChangesWarning)));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<InventoryDbContext>());
        services.AddScoped<IStockItemRepository, EfStockItemRepository>();
        services.AddScoped<IGrnRepository, EfGrnRepository>();
        services.AddScoped<IQcInspectionRepository, EfQcInspectionRepository>();
        services.AddScoped<IBatchRepository, EfBatchRepository>();
        services.AddScoped<IInventoryValueLedgerRepository, EfInventoryValueLedgerRepository>();
        services.AddScoped<IGrnReturnDraftRepository, EfGrnReturnDraftRepository>();

        services.AddOutbox<InventoryDbContext>(opts =>
        {
            var outboxConfig = configuration.GetSection("Outbox");
            opts.BatchSize = outboxConfig.GetValue<int>("BatchSize");
            opts.MaxRetries = outboxConfig.GetValue<int>("MaxRetries");
            opts.PollingIntervalSec = outboxConfig.GetValue<int>("IntervalInSeconds");
            opts.InitialBackoffSec = outboxConfig.GetValue<int>("InitialDelayInSeconds");
        });

        services.AddInbox<InventoryDbContext>(opts =>
        {
            var inboxConfig = configuration.GetSection("Inbox");
            opts.MaxRetries = inboxConfig.GetValue<int>("MaxRetries");
            opts.HandlerRetryCount = inboxConfig.GetValue<int>("MaxRetries");
        });
    }
}