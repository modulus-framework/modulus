using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Modulus.Core.Abstractions;
using Modulus.Inbox.Extensions;
using Modulus.Mediator.Extensions;
using Modulus.Outbox.Extensions;
using TradeFlow.Modules.SpendAnalysis.Application;
using TradeFlow.Modules.SpendAnalysis.Domain.Constants;
using TradeFlow.Modules.SpendAnalysis.Domain.Repositories;
using TradeFlow.Modules.SpendAnalysis.Infrastructure.Database;
using TradeFlow.Modules.SpendAnalysis.Infrastructure.Repositories;

namespace TradeFlow.Modules.SpendAnalysis.Infrastructure;

public sealed class SpendAnalysisModule : ModulusModule
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
        services.AddDbContext<SpendAnalysisDbContext>((sp, options) =>
            options
                .UseNpgsql(
                    configuration.GetConnectionString("SpendAnalysis"),
                    npgsqlOptions => npgsqlOptions
                        .MigrationsHistoryTable(
                            HistoryRepository.DefaultTableName,
                            Schemas.SpendAnalysis)
                        .UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
                .UseSnakeCaseNamingConvention()
                // ModuleDbContext's runtime model can differ from the design-time snapshot
                // (inbox/outbox model contributors, PII converters). Schema drift is gated
                // in CI via dotnet ef migrations has-pending-model-changes, so downgrade the
                // runtime migration guard to a log.
                .ConfigureWarnings(w => w.Log(RelationalEventId.PendingModelChangesWarning)));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<SpendAnalysisDbContext>());
        services.AddScoped<ICategoryTaxonomyRepository, EfCategoryTaxonomyRepository>();
        services.AddScoped<IPoLineCategoryMappingRepository, EfPoLineCategoryMappingRepository>();
        services.AddScoped<ISpendCubeRepository, EfSpendCubeRepository>();
        services.AddScoped<ISpendAnalysisUnitOfWork, EfSpendAnalysisUnitOfWork>();

        services.AddOutbox<SpendAnalysisDbContext>(opts =>
        {
            var outboxConfig = configuration.GetSection("Outbox");
            opts.BatchSize = outboxConfig.GetValue<int>("BatchSize");
            opts.MaxRetries = outboxConfig.GetValue<int>("MaxRetries");
            opts.PollingIntervalSec = outboxConfig.GetValue<int>("IntervalInSeconds");
            opts.InitialBackoffSec = outboxConfig.GetValue<int>("InitialDelayInSeconds");
        });

        services.AddInbox<SpendAnalysisDbContext>(opts =>
        {
            var inboxConfig = configuration.GetSection("Inbox");
            opts.MaxRetries = inboxConfig.GetValue<int>("MaxRetries");
            opts.HandlerRetryCount = inboxConfig.GetValue<int>("MaxRetries");
        });
    }
}
