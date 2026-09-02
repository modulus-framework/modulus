using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Modulus.Core.Abstractions;
using Modulus.Inbox.Extensions;
using Modulus.Mediator.Extensions;
using Modulus.Outbox.Extensions;
using TradeFlow.Modules.Customs.Application;
using TradeFlow.Modules.Customs.Domain.Constants;
using TradeFlow.Modules.Customs.Domain.Repositories;
using TradeFlow.Modules.Customs.Infrastructure.Database;
using TradeFlow.Modules.Customs.Infrastructure.Gateways;
using TradeFlow.Modules.Customs.Infrastructure.Repositories;
using TradeFlow.Shared.Application.Abstractions.Gateways;

namespace TradeFlow.Modules.Customs.Infrastructure;

public sealed class CustomsModule : ModulusModule
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
        services.AddDbContext<CustomsDbContext>((sp, options) =>
            options
                .UseNpgsql(
                    configuration.GetConnectionString("Customs"),
                    npgsqlOptions => npgsqlOptions
                        .MigrationsHistoryTable(
                            HistoryRepository.DefaultTableName,
                            Schemas.Customs)
                        .UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
                .UseSnakeCaseNamingConvention()
                // ModuleDbContext's runtime model can differ from the design-time snapshot
                // (inbox/outbox model contributors, PII converters). Schema drift is gated
                // in CI via dotnet ef migrations has-pending-model-changes, so downgrade the
                // runtime migration guard to a log.
                .ConfigureWarnings(w => w.Log(RelationalEventId.PendingModelChangesWarning)));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<CustomsDbContext>());
        services.AddScoped<IHsCodeRepository, EfHsCodeRepository>();
        services.AddScoped<IDutyRateRepository, EfDutyRateRepository>();
        services.AddScoped<ISroBenefitRepository, EfSroBenefitRepository>();
        services.AddScoped<IBoeRepository, EfBoeRepository>();
        services.AddScoped<IAitAtLedgerRepository, EfAitAtLedgerRepository>();
        services.AddScoped<IDemurrageRepository, EfDemurrageRepository>();
        services.AddScoped<IItemHsMappingRepository, EfItemHsMappingRepository>();
        services.AddScoped<IDutyCalculationGateway, DutyCalculationGateway>();

        services.AddOutbox<CustomsDbContext>(opts =>
        {
            var outboxConfig = configuration.GetSection("Outbox");
            opts.BatchSize = outboxConfig.GetValue<int>("BatchSize");
            opts.MaxRetries = outboxConfig.GetValue<int>("MaxRetries");
            opts.PollingIntervalSec = outboxConfig.GetValue<int>("IntervalInSeconds");
            opts.InitialBackoffSec = outboxConfig.GetValue<int>("InitialDelayInSeconds");
        });

        services.AddInbox<CustomsDbContext>(opts =>
        {
            var inboxConfig = configuration.GetSection("Inbox");
            opts.MaxRetries = inboxConfig.GetValue<int>("MaxRetries");
            opts.HandlerRetryCount = inboxConfig.GetValue<int>("MaxRetries");
        });
    }
}