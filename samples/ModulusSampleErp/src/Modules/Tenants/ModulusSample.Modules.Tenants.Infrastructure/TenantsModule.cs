using ModulusSample.Modules.Tenants.Application.IntegrationEvents;
using ModulusSample.Modules.Tenants.Domain.Constants;
using ModulusSample.Modules.Tenants.Domain.Repositories;
using ModulusSample.Modules.Tenants.Infrastructure.Database;
using ModulusSample.Modules.Tenants.Infrastructure.Repositories;
using ModulusSample.Modules.Tenants.Presentation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using FluentValidation;
using Modulus.Core.Abstractions;
using Modulus.Mediator.Extensions;
using Modulus.Authorization.Extensions;
using Modulus.EntityFrameworkCore.Abstractions;
using Modulus.Outbox.Extensions;
using Modulus.Inbox.Extensions;
using Modulus.Outbox.Abstractions;
using Modulus.Inbox.Abstractions;

namespace ModulusSample.Modules.Tenants.Infrastructure;

public sealed class TenantsModule : ModulusModule
{
    public override void ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        AddPermissions(services);
        services.AddMediatorHandlers(typeof(Application.AssemblyReference).Assembly);
        services.AddValidatorsFromAssembly(Application.AssemblyReference.Assembly);
        AddDomainEventHandlers(services);
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

    public static Type[] HandledIntegrationEvents =>
    [
        typeof(TenantCreatedIntegrationEvent),
        typeof(TenantUpdatedIntegrationEvent),
        typeof(TenantActivatedIntegrationEvent),
        typeof(TenantDeactivatedIntegrationEvent),
        typeof(TenantDeletedIntegrationEvent),
        typeof(TenantFeaturesUpdatedIntegrationEvent),
        typeof(TenantSettingsUpdatedIntegrationEvent),
    ];

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

    private static void AddDomainEventHandlers(IServiceCollection services)
    {
        Type openHandlerType = typeof(Modulus.Events.Abstractions.IDomainEventHandler<>);
        Type[] domainEventHandlers = Application.AssemblyReference.Assembly
            .GetTypes()
            .Where(t => !t.IsAbstract && !t.IsInterface && t.GetInterfaces().Any(i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == openHandlerType))
            .ToArray();

        foreach (Type domainEventHandler in domainEventHandlers)
        {
            services.AddScoped(domainEventHandler);
        }
    }
}
