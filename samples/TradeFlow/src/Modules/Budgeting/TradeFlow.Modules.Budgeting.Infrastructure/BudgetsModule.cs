using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Modulus.Core.Abstractions;
using Modulus.Inbox.Abstractions;
using Modulus.Inbox.Extensions;
using Modulus.Mediator.Extensions;
using Modulus.Outbox.Abstractions;
using Modulus.Outbox.Extensions;
using TradeFlow.Modules.Budgeting.Application;
using TradeFlow.Modules.Budgeting.Domain.Constants;
using TradeFlow.Modules.Budgeting.Domain.Repositories;
using TradeFlow.Modules.Budgeting.Infrastructure.Database;
using TradeFlow.Modules.Budgeting.Infrastructure.Gateways;
using TradeFlow.Modules.Budgeting.Infrastructure.Repositories;
using TradeFlow.Shared.Application.Abstractions.Gateways;

namespace TradeFlow.Modules.Budgeting.Infrastructure;

public sealed class BudgetsModule : ModulusModule
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
        services.AddDbContext<BudgetsDbContext>((sp, options) =>
            options
                .UseNpgsql(
                    configuration.GetConnectionString("Budgeting"),
                    npgsqlOptions => npgsqlOptions
                        .MigrationsHistoryTable(
                            HistoryRepository.DefaultTableName,
                            Schemas.Budgeting)
                        .UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
                .UseSnakeCaseNamingConvention()
                // ModuleDbContext's runtime model can differ from the design-time snapshot
                // (inbox/outbox model contributors, PII converters). Schema drift is gated
                // in CI via dotnet ef migrations has-pending-model-changes, so downgrade the
                // runtime migration guard to a log.
                .ConfigureWarnings(w => w.Log(RelationalEventId.PendingModelChangesWarning)));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<BudgetsDbContext>());
        services.AddScoped<IBudgetRepository, EfBudgetRepository>();
        services.AddScoped<IBudgetGateway, BudgetGateway>();

        services.AddOutbox<BudgetsDbContext>(opts =>
        {
            var outboxConfig = configuration.GetSection("Outbox");
            opts.BatchSize = outboxConfig.GetValue<int>("BatchSize");
            opts.MaxRetries = outboxConfig.GetValue<int>("MaxRetries");
            opts.PollingIntervalSec = outboxConfig.GetValue<int>("IntervalInSeconds");
            opts.InitialBackoffSec = outboxConfig.GetValue<int>("InitialDelayInSeconds");
        });

        services.AddInbox<BudgetsDbContext>(opts =>
        {
            var inboxConfig = configuration.GetSection("Inbox");
            opts.MaxRetries = inboxConfig.GetValue<int>("MaxRetries");
            opts.HandlerRetryCount = inboxConfig.GetValue<int>("MaxRetries");
        });
    }
}