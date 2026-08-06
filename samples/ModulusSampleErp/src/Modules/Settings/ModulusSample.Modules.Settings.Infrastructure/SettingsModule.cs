using ModulusSample.Modules.Settings.Application.DomainEventHandlers;
using ModulusSample.Modules.Settings.Application.IntegrationEvents;
using ModulusSample.Modules.Settings.Application.Settings.Queries;
using ModulusSample.Modules.Settings.Application.Abstractions;
using ModulusSample.Modules.Settings.Domain.Repositories;
using ModulusSample.Modules.Settings.Infrastructure.Configurations;
using ModulusSample.Modules.Settings.Infrastructure.Database;
using ModulusSample.Modules.Settings.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using FluentValidation;
using Modulus.Core.Abstractions;
using Modulus.Mediator.Extensions;
using ModulusSample.Modules.Settings.Application;

namespace ModulusSample.Modules.Settings.Infrastructure;

public sealed class SettingsModule : ModulusModule
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
        typeof(SettingCreatedIntegrationEvent),
        typeof(SettingUpdatedIntegrationEvent),
        typeof(SettingDeletedIntegrationEvent),
    ];

    private static void AddInfrastructure(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<SettingsDbContext>((sp, options) =>
            options
                .UseNpgsql(
                    configuration.GetConnectionString("Database"),
                    npgsqlOptions => npgsqlOptions
                        .MigrationsHistoryTable(HistoryRepository.DefaultTableName, Database.Schemas.Settings)
                        .UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
                .UseSnakeCaseNamingConvention());

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<SettingsDbContext>());

        services.AddScoped<ISettingRepository, EfSettingRepository>();
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