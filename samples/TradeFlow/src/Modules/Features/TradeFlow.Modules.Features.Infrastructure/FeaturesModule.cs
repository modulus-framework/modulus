using ModulusSample.Modules.Features.Application;
using ModulusSample.Modules.Features.Application.IntegrationEvents;
using ModulusSample.Modules.Features.Domain.Repositories;
using ModulusSample.Modules.Features.Infrastructure.Database;
using ModulusSample.Modules.Features.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Diagnostics;
using FluentValidation;
using Modulus.Core.Abstractions;
using Modulus.EntityFrameworkCore.Abstractions;
using Modulus.Mediator.Extensions;
using Modulus.Outbox.Extensions;
using Modulus.Inbox.Extensions;
using Modulus.Outbox.Abstractions;
using Modulus.Inbox.Abstractions;

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
                .UseSnakeCaseNamingConvention()
                // ModuleDbContext's runtime model can differ from the design-time snapshot
                // (inbox/outbox model contributors, PII converters). Schema drift is gated
                // in CI via dotnet ef migrations has-pending-model-changes, so downgrade the
                // runtime migration guard to a log.
                .ConfigureWarnings(w => w.Log(RelationalEventId.PendingModelChangesWarning)));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<FeaturesDbContext>());

        services.AddScoped<IFeatureFlagRepository, EfFeatureFlagRepository>();

        services.AddOutbox<FeaturesDbContext>(opts =>
        {
            var outboxConfig = configuration.GetSection("Outbox");
            opts.BatchSize = outboxConfig.GetValue<int>("BatchSize");
            opts.MaxRetries = outboxConfig.GetValue<int>("MaxRetries");
            opts.PollingIntervalSec = outboxConfig.GetValue<int>("IntervalInSeconds");
            opts.InitialBackoffSec = outboxConfig.GetValue<int>("InitialDelayInSeconds");
        });

        services.AddInbox<FeaturesDbContext>(opts =>
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
