using TradeFlow.Modules.WorkflowEngine.Application;
using TradeFlow.Modules.WorkflowEngine.Domain.Constants;
using TradeFlow.Modules.WorkflowEngine.Domain.Repositories;
using TradeFlow.Modules.WorkflowEngine.Infrastructure.Database;
using TradeFlow.Modules.WorkflowEngine.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using FluentValidation;
using Modulus.Core.Abstractions;
using Modulus.Mediator.Extensions;
using Modulus.Outbox.Extensions;
using Modulus.Inbox.Extensions;

namespace TradeFlow.Modules.WorkflowEngine.Infrastructure;

public sealed class WorkflowEngineModule : ModulusModule
{
    public override void ConfigureServices(
        IServiceCollection services, IConfiguration configuration)
    {
        services.AddMediatorHandlers(typeof(Application.AssemblyReference).Assembly);
        services.AddValidatorsFromAssembly(Application.AssemblyReference.Assembly, includeInternalTypes: true);
        AddInfrastructure(services, configuration);
    }

    private static void AddInfrastructure(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<WorkflowDbContext>((sp, options) =>
            options
                .UseNpgsql(
                    configuration.GetConnectionString("WorkflowEngine"),
                    npgsqlOptions => npgsqlOptions
                        .MigrationsHistoryTable(HistoryRepository.DefaultTableName, Schemas.WorkflowEngine)
                        .UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
                .UseSnakeCaseNamingConvention());

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<WorkflowDbContext>());
        services.AddScoped<IWorkflowDefinitionRepository, EfWorkflowDefinitionRepository>();
        services.AddScoped<IWorkflowInstanceRepository, EfWorkflowInstanceRepository>();
        services.AddScoped<IWorkflowTaskRepository, EfWorkflowTaskRepository>();

        services.AddOutbox<WorkflowDbContext>(opts =>
        {
            var outboxConfig = configuration.GetSection("Outbox");
            opts.BatchSize = outboxConfig.GetValue<int>("BatchSize");
            opts.MaxRetries = outboxConfig.GetValue<int>("MaxRetries");
            opts.PollingIntervalSec = outboxConfig.GetValue<int>("IntervalInSeconds");
            opts.InitialBackoffSec = outboxConfig.GetValue<int>("InitialDelayInSeconds");
        });

        services.AddInbox<WorkflowDbContext>(opts =>
        {
            var inboxConfig = configuration.GetSection("Inbox");
            opts.MaxRetries = inboxConfig.GetValue<int>("MaxRetries");
            opts.HandlerRetryCount = inboxConfig.GetValue<int>("MaxRetries");
        });
    }
}
