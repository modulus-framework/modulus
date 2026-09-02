using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Modulus.Core.Abstractions;
using Modulus.Mediator.Extensions;
using Modulus.Outbox.Extensions;
using Modulus.Inbox.Extensions;
using TradeFlow.Modules.Finance.Application;
using TradeFlow.Modules.Finance.Application.Handlers;
using TradeFlow.Modules.Finance.Domain.Repositories;
using TradeFlow.Modules.Finance.Infrastructure.Database;
using TradeFlow.Modules.Finance.Infrastructure.Repositories;

namespace TradeFlow.Modules.Finance.Infrastructure;

public sealed class FinanceModule : ModulusModule
{
    public override void ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<FinanceDbContext>((sp, options) =>
            options
                .UseNpgsql(
                    configuration.GetConnectionString("Finance"),
                    npgsqlOptions => npgsqlOptions
                        .MigrationsHistoryTable(
                            HistoryRepository.DefaultTableName,
                            "finance")
                        .UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
                .UseSnakeCaseNamingConvention()
                // ModuleDbContext's runtime model can differ from the design-time snapshot
                // (inbox/outbox model contributors, PII converters). Schema drift is gated
                // in CI via dotnet ef migrations has-pending-model-changes, so downgrade the
                // runtime migration guard to a log.
                .ConfigureWarnings(w => w.Log(RelationalEventId.PendingModelChangesWarning)));

        services.AddScoped<IFinanceUnitOfWork>(sp => sp.GetRequiredService<FinanceDbContext>());
        services.AddScoped<IApInvoiceRepository, EfApInvoiceRepository>();
        services.AddScoped<IFxRateRepository, EfFxRateRepository>();
        services.AddScoped<ICostCenterRepository, EfCostCenterRepository>();
        services.AddScoped<IPaymentProposalRepository, EfPaymentProposalRepository>();
        services.AddScoped<IJournalBatchRepository, EfJournalBatchRepository>();
        services.AddScoped<IMatchExceptionRepository, EfMatchExceptionRepository>();
        services.AddScoped<IGrIrAccrualRepository, EfGrIrAccrualRepository>();

        services.AddMediatorHandlers(typeof(CreateApInvoiceCommandHandler).Assembly);

        services.AddOutbox<FinanceDbContext>(opts =>
        {
            var outboxConfig = configuration.GetSection("Outbox");
            opts.BatchSize = outboxConfig.GetValue<int>("BatchSize");
            opts.MaxRetries = outboxConfig.GetValue<int>("MaxRetries");
            opts.PollingIntervalSec = outboxConfig.GetValue<int>("IntervalInSeconds");
            opts.InitialBackoffSec = outboxConfig.GetValue<int>("InitialDelayInSeconds");
        });

        services.AddInbox<FinanceDbContext>(opts =>
        {
            var inboxConfig = configuration.GetSection("Inbox");
            opts.MaxRetries = inboxConfig.GetValue<int>("MaxRetries");
            opts.HandlerRetryCount = inboxConfig.GetValue<int>("MaxRetries");
        });
    }
}