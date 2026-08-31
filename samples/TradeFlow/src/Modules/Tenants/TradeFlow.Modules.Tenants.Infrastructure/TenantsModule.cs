using TradeFlow.Modules.Tenants.Application;
using TradeFlow.Modules.Tenants.Domain.Constants;
using TradeFlow.Modules.Tenants.Domain.Repositories;
using TradeFlow.Modules.Tenants.Infrastructure.Database;
using TradeFlow.Modules.Tenants.Infrastructure.Repositories;
using TradeFlow.Modules.Tenants.Presentation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using FluentValidation;
using Modulus.Core.Abstractions;
using Modulus.Mediator.Extensions;
using Modulus.Authorization.Extensions;
using Modulus.Outbox.Extensions;
using Modulus.Inbox.Extensions;

namespace TradeFlow.Modules.Tenants.Infrastructure;

public sealed class TenantsModule : ModulusModule
{
    public override void ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        AddPermissions(services);
        services.AddMediatorHandlers(typeof(Application.AssemblyReference).Assembly);
        services.AddValidatorsFromAssembly(Application.AssemblyReference.Assembly);
        AddInfrastructure(services, configuration);
    }

    private static void AddPermissions(IServiceCollection services)
    {
        services.AddPermissions("Tenants", registry =>
        {
            registry.Add(TenantPermissions.TenantViewAll, "View all tenants");
            registry.Add(TenantPermissions.TenantManageAll, "Manage all tenants");
            registry.Add(TenantPermissions.TenantCreate, "Create tenants");
            registry.Add(TenantPermissions.TenantUpdate, "Update tenants");
            registry.Add(TenantPermissions.TenantDelete, "Delete tenants");
            registry.Add(TenantPermissions.TenantAdmin, "Tenant administrator access");
        });
    }

    private static void AddInfrastructure(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<TenantsDbContext>((sp, options) =>
            options
                .UseNpgsql(
                    configuration.GetConnectionString("Tenants"),
                    npgsqlOptions => npgsqlOptions
                        .MigrationsHistoryTable(
                            HistoryRepository.DefaultTableName,
                            Schemas.Tenants)
                        .UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
                .UseSnakeCaseNamingConvention());

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<TenantsDbContext>());

        services.AddScoped<ITenantRepository, EfTenantRepository>();

        services.AddOutbox<TenantsDbContext>(opts =>
        {
            var outboxConfig = configuration.GetSection("Outbox");
            opts.BatchSize = outboxConfig.GetValue<int>("BatchSize");
            opts.MaxRetries = outboxConfig.GetValue<int>("MaxRetries");
            opts.PollingIntervalSec = outboxConfig.GetValue<int>("IntervalInSeconds");
            opts.InitialBackoffSec = outboxConfig.GetValue<int>("InitialDelayInSeconds");
        });

        services.AddInbox<TenantsDbContext>(opts =>
        {
            var inboxConfig = configuration.GetSection("Inbox");
            opts.MaxRetries = inboxConfig.GetValue<int>("MaxRetries");
            opts.HandlerRetryCount = inboxConfig.GetValue<int>("MaxRetries");
        });
    }
}
