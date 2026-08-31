using TradeFlow.Modules.Vendors.Application;
using TradeFlow.Modules.Vendors.Application.Abstractions;
using TradeFlow.Modules.Vendors.Domain.Constants;
using TradeFlow.Modules.Vendors.Domain.Repositories;
using TradeFlow.Modules.Vendors.Infrastructure.Database;
using TradeFlow.Modules.Vendors.Infrastructure.PublicApi;
using TradeFlow.Modules.Vendors.Infrastructure.Repositories;
using TradeFlow.Modules.Vendors.PublicApi;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using FluentValidation;
using Modulus.Core.Abstractions;
using Modulus.Mediator.Extensions;
using Modulus.Outbox.Extensions;
using Modulus.Inbox.Extensions;
using Modulus.Outbox.Abstractions;
using Modulus.Inbox.Abstractions;

namespace TradeFlow.Modules.Vendors.Infrastructure;

public sealed class VendorsModule : ModulusModule
{
    public override void ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddMediatorHandlers(typeof(Application.AssemblyReference).Assembly);
        services.AddValidatorsFromAssembly(Application.AssemblyReference.Assembly, includeInternalTypes: true);
        AddInfrastructure(services, configuration);
    }

    private static void AddInfrastructure(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<VendorsDbContext>((sp, options) =>
            options
                .UseNpgsql(
                    configuration.GetConnectionString("Vendors"),
                    npgsqlOptions => npgsqlOptions
                        .MigrationsHistoryTable(
                            HistoryRepository.DefaultTableName,
                            Schemas.Vendors)
                        .UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
                .UseSnakeCaseNamingConvention());

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<VendorsDbContext>());
        services.AddScoped<IVendorRepository, EfVendorRepository>();
        services.AddScoped<IVendorPublicApi, VendorPublicApi>();

        services.AddOutbox<VendorsDbContext>(opts =>
        {
            var outboxConfig = configuration.GetSection("Outbox");
            opts.BatchSize = outboxConfig.GetValue<int>("BatchSize");
            opts.MaxRetries = outboxConfig.GetValue<int>("MaxRetries");
            opts.PollingIntervalSec = outboxConfig.GetValue<int>("IntervalInSeconds");
            opts.InitialBackoffSec = outboxConfig.GetValue<int>("InitialDelayInSeconds");
        });

        services.AddInbox<VendorsDbContext>(opts =>
        {
            var inboxConfig = configuration.GetSection("Inbox");
            opts.MaxRetries = inboxConfig.GetValue<int>("MaxRetries");
            opts.HandlerRetryCount = inboxConfig.GetValue<int>("MaxRetries");
        });
    }
}
