using ModulusSample.Modules.Notifications.Application;
using ModulusSample.Modules.Notifications.Application.Abstractions;
using ModulusSample.Modules.Notifications.Application.IntegrationEvents;
using ModulusSample.Modules.Notifications.Domain.Repositories;
using ModulusSample.Modules.Notifications.Infrastructure.Database;
using ModulusSample.Modules.Notifications.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using FluentValidation;
using Modulus.Core.Abstractions;
using Modulus.Mediator.Extensions;

namespace ModulusSample.Modules.Notifications.Infrastructure;

public sealed class NotificationsModule : ModulusModule
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
        typeof(NotificationCreatedIntegrationEvent),
        typeof(NotificationReadIntegrationEvent),
    ];

    private static void AddInfrastructure(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<NotificationsDbContext>((sp, options) =>
            options
                .UseNpgsql(
                    configuration.GetConnectionString("Notifications"),
                    npgsqlOptions => npgsqlOptions
                        .MigrationsHistoryTable(HistoryRepository.DefaultTableName, Database.Schemas.Notifications)
                        .UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
                .UseSnakeCaseNamingConvention());

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<NotificationsDbContext>());

        services.AddScoped<INotificationRepository, EfNotificationRepository>();
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