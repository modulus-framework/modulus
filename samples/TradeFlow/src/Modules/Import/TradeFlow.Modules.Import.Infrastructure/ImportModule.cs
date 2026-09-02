using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Modulus.Core.Abstractions;
using Modulus.Inbox.Extensions;
using Modulus.Mediator.Extensions;
using Modulus.Outbox.Extensions;
using TradeFlow.Modules.Import.Application;
using TradeFlow.Modules.Import.Domain.Constants;
using TradeFlow.Modules.Import.Domain.Repositories;
using TradeFlow.Modules.Import.Infrastructure.Database;
using TradeFlow.Modules.Import.Infrastructure.Repositories;
using TradeFlow.Shared.Application.Abstractions.Import;
using TradeFlow.Shared.Infrastructure.Import;

namespace TradeFlow.Modules.Import.Infrastructure;

public sealed class ImportModule : ModulusModule
{
    public override void ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddMediatorHandlers(typeof(Application.AssemblyReference).Assembly);
        services.AddValidatorsFromAssembly(Application.AssemblyReference.Assembly);
        services.AddSingleton<IFeasibilityEngine, HeuristicFeasibilityEngine>();
        AddInfrastructure(services, configuration);
    }

    private static void AddInfrastructure(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<ImportDbContext>((sp, options) =>
            options
                .UseNpgsql(
                    configuration.GetConnectionString("Import"),
                    npgsqlOptions => npgsqlOptions
                        .MigrationsHistoryTable(
                            HistoryRepository.DefaultTableName,
                            Schemas.Import)
                        .UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
                .UseSnakeCaseNamingConvention()
                // ModuleDbContext's runtime model can differ from the design-time snapshot
                // (inbox/outbox model contributors, PII converters). Schema drift is gated
                // in CI via dotnet ef migrations has-pending-model-changes, so downgrade the
                // runtime migration guard to a log.
                .ConfigureWarnings(w => w.Log(RelationalEventId.PendingModelChangesWarning)));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<ImportDbContext>());
        services.AddScoped<IImportFileRepository, EfImportFileRepository>();
        services.AddScoped<IProformaInvoiceRepository, EfProformaInvoiceRepository>();
        services.AddScoped<ICommercialInvoiceRepository, EfCommercialInvoiceRepository>();
        services.AddScoped<IPackingListRepository, EfPackingListRepository>();
        services.AddScoped<IShipmentRepository, EfShipmentRepository>();
        services.AddScoped<ITransportDocumentRepository, EfTransportDocumentRepository>();
        services.AddScoped<IFreightCostRepository, EfFreightCostRepository>();
        services.AddScoped<IInsurancePolicyRepository, EfInsurancePolicyRepository>();
        services.AddScoped<IImportPermitRepository, EfImportPermitRepository>();
        services.AddScoped<IBillOfEntryRepository, EfBillOfEntryRepository>();
        services.AddScoped<IAssessmentVarianceRepository, EfAssessmentVarianceRepository>();
        services.AddScoped<IPortChargeRepository, EfPortChargeRepository>();
        services.AddScoped<ICnfAgentRepository, EfCnfAgentRepository>();
        services.AddScoped<IImportPlanRepository, EfImportPlanRepository>();
        services.AddScoped<ICertificateOfOriginRepository, EfCertificateOfOriginRepository>();
        services.AddScoped<ICooIssuerRegistryRepository, EfCooIssuerRegistryRepository>();

        services.AddOutbox<ImportDbContext>(opts =>
        {
            var outboxConfig = configuration.GetSection("Outbox");
            opts.BatchSize = outboxConfig.GetValue<int>("BatchSize");
            opts.MaxRetries = outboxConfig.GetValue<int>("MaxRetries");
            opts.PollingIntervalSec = outboxConfig.GetValue<int>("IntervalInSeconds");
            opts.InitialBackoffSec = outboxConfig.GetValue<int>("InitialDelayInSeconds");
        });

        services.AddInbox<ImportDbContext>(opts =>
        {
            var inboxConfig = configuration.GetSection("Inbox");
            opts.MaxRetries = inboxConfig.GetValue<int>("MaxRetries");
            opts.HandlerRetryCount = inboxConfig.GetValue<int>("MaxRetries");
        });
    }
}