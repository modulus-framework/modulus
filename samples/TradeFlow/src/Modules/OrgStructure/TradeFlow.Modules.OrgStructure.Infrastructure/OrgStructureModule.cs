using TradeFlow.Modules.OrgStructure.Application;
using TradeFlow.Modules.OrgStructure.Application.Abstractions;
using TradeFlow.Modules.OrgStructure.Domain.Constants;
using TradeFlow.Modules.OrgStructure.Domain.Repositories;
using TradeFlow.Modules.OrgStructure.Infrastructure.Database;
using TradeFlow.Modules.OrgStructure.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using FluentValidation;
using Modulus.Core.Abstractions;
using Modulus.Mediator.Extensions;
using Modulus.Outbox.Extensions;
using Modulus.Inbox.Extensions;

namespace TradeFlow.Modules.OrgStructure.Infrastructure;

public sealed class OrgStructureModule : ModulusModule
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
        services.AddDbContext<OrgStructureDbContext>((sp, options) =>
            options
                .UseNpgsql(
                    configuration.GetConnectionString("OrgStructure"),
                    npgsqlOptions => npgsqlOptions
                        .MigrationsHistoryTable(HistoryRepository.DefaultTableName, Schemas.OrgStructure)
                        .UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
                .UseSnakeCaseNamingConvention());

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<OrgStructureDbContext>());
        services.AddScoped<IOrgNodeRepository, EfOrgNodeRepository>();
        services.AddScoped<IPositionRepository, EfPositionRepository>();

        services.AddOutbox<OrgStructureDbContext>(opts =>
        {
            var outboxConfig = configuration.GetSection("Outbox");
            opts.BatchSize = outboxConfig.GetValue<int>("BatchSize");
            opts.MaxRetries = outboxConfig.GetValue<int>("MaxRetries");
            opts.PollingIntervalSec = outboxConfig.GetValue<int>("IntervalInSeconds");
            opts.InitialBackoffSec = outboxConfig.GetValue<int>("InitialDelayInSeconds");
        });

        services.AddInbox<OrgStructureDbContext>(opts =>
        {
            var inboxConfig = configuration.GetSection("Inbox");
            opts.MaxRetries = inboxConfig.GetValue<int>("MaxRetries");
            opts.HandlerRetryCount = inboxConfig.GetValue<int>("MaxRetries");
        });
    }
}
