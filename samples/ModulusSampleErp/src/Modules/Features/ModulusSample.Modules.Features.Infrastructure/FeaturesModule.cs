using ModulusSample.Modules.Features.Application;
using ModulusSample.Modules.Features.Application.Abstractions;
using ModulusSample.Modules.Features.Application.DomainEventHandlers;
using ModulusSample.Modules.Features.Application.IntegrationEvents;
using ModulusSample.Modules.Features.Domain.Repositories;
using ModulusSample.Modules.Features.Infrastructure.Database;
using ModulusSample.Modules.Features.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using FluentValidation;
using Modulus.Core.Abstractions;
using Modulus.Mediator.Extensions;

namespace ModulusSample.Modules.Features.Infrastructure;

public sealed class FeaturesModule : ModulusModule
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
        typeof(FeatureFlagCreatedIntegrationEvent),
        typeof(FeatureFlagUpdatedIntegrationEvent),
        typeof(FeatureFlagDeletedIntegrationEvent),
        typeof(FeatureFlagToggledIntegrationEvent),
    ];

    private static void AddInfrastructure(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<FeaturesDbContext>((sp, options) =>
            options
                .UseNpgsql(
                    configuration.GetConnectionString("Features"),
                    npgsqlOptions => npgsqlOptions
                        .MigrationsHistoryTable(HistoryRepository.DefaultTableName, Schemas.Features)
                        .UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
                .UseSnakeCaseNamingConvention());

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<FeaturesDbContext>());

        services.AddScoped<IFeatureFlagRepository, EfFeatureFlagRepository>();
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
