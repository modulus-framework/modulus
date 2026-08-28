using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Modulus.Core.Abstractions;
using Modulus.Inbox.Extensions;
using Modulus.Mediator.Extensions;
using Modulus.Outbox.Extensions;
using ProcureFlow.Modules.Customs.Application;
using ProcureFlow.Modules.Customs.Domain.Constants;
using ProcureFlow.Modules.Customs.Domain.Repositories;
using ProcureFlow.Modules.Customs.Infrastructure.Database;
using ProcureFlow.Modules.Customs.Infrastructure.Gateways;
using ProcureFlow.Modules.Customs.Infrastructure.Repositories;
using ProcureFlow.Shared.Application.Abstractions.Gateways;

namespace ProcureFlow.Modules.Customs.Infrastructure;

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
                .UseSnakeCaseNamingConvention());

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