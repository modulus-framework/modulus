using TradeFlow.Modules.Configuration.Application.Settings.Queries;
using TradeFlow.Modules.Configuration.Domain.Repositories;
using TradeFlow.Modules.Configuration.Infrastructure.Configurations;
using TradeFlow.Modules.Configuration.Infrastructure.Database;
using TradeFlow.Modules.Configuration.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using FluentValidation;
using Modulus.Core.Abstractions;
using Modulus.Mediator.Extensions;
using TradeFlow.Modules.Configuration.Application;
using Modulus.Outbox.Extensions;
using Modulus.Inbox.Extensions;
using Modulus.Outbox.Abstractions;
using Modulus.Inbox.Abstractions;

namespace TradeFlow.Modules.Configuration.Infrastructure;

public sealed class ConfigurationModule : ModulusModule
{
    public override void ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddMediatorHandlers(typeof(AssemblyReference).Assembly);
        services.AddValidatorsFromAssembly(Application.AssemblyReference.Assembly);
        AddInfrastructure(services, configuration);
    }

    private static void AddInfrastructure(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<ConfigurationDbContext>((sp, options) =>
            options
                .UseNpgsql(
                    configuration.GetConnectionString("Configuration"),
                    npgsqlOptions => npgsqlOptions
                        .MigrationsHistoryTable(HistoryRepository.DefaultTableName, Schemas.Configuration)
                        .UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
                .UseSnakeCaseNamingConvention());

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<ConfigurationDbContext>());

        services.AddScoped<ISettingRepository, EfSettingRepository>();
        services.AddScoped<IFeatureFlagRepository, EfFeatureFlagRepository>();

        services.AddOutbox<ConfigurationDbContext>(opts =>
        {
            var outboxConfig = configuration.GetSection("Outbox");
            opts.BatchSize = outboxConfig.GetValue<int>("BatchSize");
            opts.MaxRetries = outboxConfig.GetValue<int>("MaxRetries");
            opts.PollingIntervalSec = outboxConfig.GetValue<int>("IntervalInSeconds");
            opts.InitialBackoffSec = outboxConfig.GetValue<int>("InitialDelayInSeconds");
        });

        services.AddInbox<ConfigurationDbContext>(opts =>
        {
            var inboxConfig = configuration.GetSection("Inbox");
            opts.MaxRetries = inboxConfig.GetValue<int>("MaxRetries");
            opts.HandlerRetryCount = inboxConfig.GetValue<int>("MaxRetries");
        });
    }
}