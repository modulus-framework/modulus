using ModulusSample.Modules.VirtualFileExplorer.Application;
using ModulusSample.Modules.VirtualFileExplorer.Application.Abstractions;
using ModulusSample.Modules.VirtualFileExplorer.Application.DomainEventHandlers;
using ModulusSample.Modules.VirtualFileExplorer.Application.IntegrationEvents;
using ModulusSample.Modules.VirtualFileExplorer.Domain.Repositories;
using ModulusSample.Modules.VirtualFileExplorer.Infrastructure.Database;
using ModulusSample.Modules.VirtualFileExplorer.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using FluentValidation;
using Modulus.Core.Abstractions;
using Modulus.Mediator.Extensions;

namespace ModulusSample.Modules.VirtualFileExplorer.Infrastructure;

public sealed class VirtualFileExplorerModule : ModulusModule
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
        typeof(VirtualFolderCreatedIntegrationEvent),
        typeof(VirtualFolderDeletedIntegrationEvent),
        typeof(VirtualFileUploadedIntegrationEvent),
        typeof(VirtualFileDeletedIntegrationEvent),
    ];

    private static void AddInfrastructure(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<VirtualFileExplorerDbContext>((sp, options) =>
            options
                .UseNpgsql(
                    configuration.GetConnectionString("VirtualFileExplorer"),
                    npgsqlOptions => npgsqlOptions
                        .MigrationsHistoryTable(HistoryRepository.DefaultTableName, Database.Schemas.VirtualFileExplorer)
                        .UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
                .UseSnakeCaseNamingConvention());

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<VirtualFileExplorerDbContext>());

        services.AddScoped<IVirtualFolderRepository, EfVirtualFolderRepository>();
        services.AddScoped<IVirtualFileRepository, EfVirtualFileRepository>();
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
