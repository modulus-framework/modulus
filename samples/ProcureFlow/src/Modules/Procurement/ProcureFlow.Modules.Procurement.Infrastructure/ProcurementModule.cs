using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Modulus.Core.Abstractions;
using Modulus.Inbox.Extensions;
using Modulus.Mediator.Extensions;
using Modulus.Outbox.Extensions;
using ProcureFlow.Modules.Procurement.Application;
using ProcureFlow.Modules.Procurement.Domain.Constants;
using ProcureFlow.Modules.Procurement.Domain.Repositories;
using ProcureFlow.Modules.Procurement.Infrastructure.Database;
using ProcureFlow.Modules.Procurement.Infrastructure.Repositories;

namespace ProcureFlow.Modules.Procurement.Infrastructure;

public sealed class ProcurementModule : ModulusModule
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
        services.AddDbContext<ProcurementDbContext>((sp, options) =>
            options
                .UseNpgsql(
                    configuration.GetConnectionString("Procurement"),
                    npgsqlOptions => npgsqlOptions
                        .MigrationsHistoryTable(
                            HistoryRepository.DefaultTableName,
                            Schemas.Procurement)
                        .UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
                .UseSnakeCaseNamingConvention());

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<ProcurementDbContext>());
        services.AddScoped<IPrRepository, EfPrRepository>();
        services.AddScoped<IRfqRepository, EfRfqRepository>();
        services.AddScoped<IPoRepository, EfPoRepository>();
        services.AddScoped<IContractRepository, EfContractRepository>();

        services.AddOutbox<ProcurementDbContext>(opts =>
        {
            var outboxConfig = configuration.GetSection("Outbox");
            opts.BatchSize = outboxConfig.GetValue<int>("BatchSize");
            opts.MaxRetries = outboxConfig.GetValue<int>("MaxRetries");
            opts.PollingIntervalSec = outboxConfig.GetValue<int>("IntervalInSeconds");
            opts.InitialBackoffSec = outboxConfig.GetValue<int>("InitialDelayInSeconds");
        });

        services.AddInbox<ProcurementDbContext>(opts =>
        {
            var inboxConfig = configuration.GetSection("Inbox");
            opts.MaxRetries = inboxConfig.GetValue<int>("MaxRetries");
            opts.HandlerRetryCount = inboxConfig.GetValue<int>("MaxRetries");
        });
    }
}