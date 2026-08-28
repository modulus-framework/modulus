using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Modulus.Core.Abstractions;
using Modulus.Inbox.Extensions;
using Modulus.Mediator.Extensions;
using Modulus.Outbox.Extensions;
using ProcureFlow.Modules.TradeFinance.Application;
using ProcureFlow.Modules.TradeFinance.Domain.Constants;
using ProcureFlow.Modules.TradeFinance.Domain.Repositories;
using ProcureFlow.Modules.TradeFinance.Infrastructure.Database;
using ProcureFlow.Modules.TradeFinance.Infrastructure.Repositories;

namespace ProcureFlow.Modules.TradeFinance.Infrastructure;

public sealed class TradeFinanceModule : ModulusModule
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
        services.AddDbContext<TradeFinanceDbContext>((sp, options) =>
            options
                .UseNpgsql(
                    configuration.GetConnectionString("TradeFinance"),
                    npgsqlOptions => npgsqlOptions
                        .MigrationsHistoryTable(
                            HistoryRepository.DefaultTableName,
                            Schemas.TradeFinance)
                        .UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
                .UseSnakeCaseNamingConvention());

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<TradeFinanceDbContext>());
        services.AddScoped<ILcRepository, EfLcRepository>();
        services.AddScoped<ITtRepository, EfTtRepository>();
        services.AddScoped<ISwiftMessageRepository, EfSwiftMessageRepository>();
        services.AddScoped<IBankFacilityRepository, EfBankFacilityRepository>();
        services.AddScoped<IPaymentObligationRepository, EfPaymentObligationRepository>();

        services.AddOutbox<TradeFinanceDbContext>(opts =>
        {
            var outboxConfig = configuration.GetSection("Outbox");
            opts.BatchSize = outboxConfig.GetValue<int>("BatchSize");
            opts.MaxRetries = outboxConfig.GetValue<int>("MaxRetries");
            opts.PollingIntervalSec = outboxConfig.GetValue<int>("IntervalInSeconds");
            opts.InitialBackoffSec = outboxConfig.GetValue<int>("InitialDelayInSeconds");
        });

        services.AddInbox<TradeFinanceDbContext>(opts =>
        {
            var inboxConfig = configuration.GetSection("Inbox");
            opts.MaxRetries = inboxConfig.GetValue<int>("MaxRetries");
            opts.HandlerRetryCount = inboxConfig.GetValue<int>("MaxRetries");
        });
    }
}