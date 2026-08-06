using ModulusSample.Modules.Tenants.Application.DomainEventHandlers;
using ModulusSample.Modules.Tenants.Application.IntegrationEvents;
using ModulusSample.Modules.Tenants.Application.Abstractions;
using ModulusSample.Modules.Tenants.Domain.Constants;
using ModulusSample.Modules.Tenants.Domain.Repositories;
using ModulusSample.Modules.Tenants.Infrastructure.Configurations;
using ModulusSample.Modules.Tenants.Infrastructure.Database;
using ModulusSample.Modules.Tenants.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using FluentValidation;
using Modulus.Core.Abstractions;
using Modulus.Mediator.Extensions;
using ModulusSample.Modules.Tenants.Application;

namespace ModulusSample.Modules.Tenants.Infrastructure;

public sealed class TenantsModule : ModulusModule
{
    public override void ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddMediatorHandlers(typeof(AssemblyReference).Assembly);
        services.AddValidatorsFromAssembly(Application.AssemblyReference.Assembly);
        AddDomainEventHandlers(services);
        AddInfrastructure(services, configuration);
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