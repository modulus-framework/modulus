using TradeFlow.Modules.VirtualFileExplorer.Application;
using Modulus.EntityFrameworkCore.Abstractions;
using TradeFlow.Modules.VirtualFileExplorer.Application.DomainEventHandlers;
using TradeFlow.Modules.VirtualFileExplorer.Application.IntegrationEvents;
using TradeFlow.Modules.VirtualFileExplorer.Domain.Repositories;
using TradeFlow.Modules.VirtualFileExplorer.Infrastructure.Database;
using TradeFlow.Modules.VirtualFileExplorer.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using FluentValidation;
using Modulus.Core.Abstractions;
using Modulus.Mediator.Extensions;
using Modulus.Outbox.Extensions;
using Modulus.Inbox.Extensions;
using Modulus.Outbox.Abstractions;
using Modulus.Inbox.Abstractions;

namespace TradeFlow.Modules.VirtualFileExplorer.Infrastructure;

public sealed class VirtualFileExplorerModule : ModulusModule
{
    public override void ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddMediatorHandlers(typeof(AssemblyReference).Assembly);
        services.AddValidatorsFromAssembly(Application.AssemblyReference.Assembly);
        // NOTE: IDomainEventHandler registrations are global — the host's
        // AddModulusEvents scans every module Application assembly. A local
        // registration loop here would double-register (and double-publish).
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

        services.AddOutbox<VirtualFileExplorerDbContext>(opts =>
        {
            var outboxConfig = configuration.GetSection("Outbox");
            opts.BatchSize = outboxConfig.GetValue<int>("BatchSize");
            opts.MaxRetries = outboxConfig.GetValue<int>("MaxRetries");
            opts.PollingIntervalSec = outboxConfig.GetValue<int>("IntervalInSeconds");
            opts.InitialBackoffSec = outboxConfig.GetValue<int>("InitialDelayInSeconds");
        });

        services.AddInbox<VirtualFileExplorerDbContext>(opts =>
        {
            var inboxConfig = configuration.GetSection("Inbox");
            opts.MaxRetries = inboxConfig.GetValue<int>("MaxRetries");
            opts.HandlerRetryCount = inboxConfig.GetValue<int>("MaxRetries");
        });
    }
}
