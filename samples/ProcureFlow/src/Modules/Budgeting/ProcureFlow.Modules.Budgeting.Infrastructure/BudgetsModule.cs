using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Modulus.Core.Abstractions;
using Modulus.Inbox.Abstractions;
using Modulus.Inbox.Extensions;
using Modulus.Mediator.Extensions;
using Modulus.Outbox.Abstractions;
using Modulus.Outbox.Extensions;
using ProcureFlow.Modules.Budgeting.Application;
using ProcureFlow.Modules.Budgeting.Domain.Constants;
using ProcureFlow.Modules.Budgeting.Domain.Repositories;
using ProcureFlow.Modules.Budgeting.Infrastructure.Database;
using ProcureFlow.Modules.Budgeting.Infrastructure.Gateways;
using ProcureFlow.Modules.Budgeting.Infrastructure.Repositories;
using ProcureFlow.Shared.Application.Abstractions.Gateways;

namespace ProcureFlow.Modules.Budgeting.Infrastructure;

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
                .UseSnakeCaseNamingConvention());

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