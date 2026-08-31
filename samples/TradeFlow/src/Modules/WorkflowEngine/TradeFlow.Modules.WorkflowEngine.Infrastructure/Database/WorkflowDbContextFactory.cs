using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using TradeFlow.Modules.WorkflowEngine.Domain.Constants;
using Modulus.EntityFrameworkCore.Design;

namespace TradeFlow.Modules.WorkflowEngine.Infrastructure.Database;

public sealed class WorkflowDbContextFactory : IDesignTimeDbContextFactory<WorkflowDbContext>
{
    public WorkflowDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<WorkflowDbContext>();

        string? connectionString = Environment.GetEnvironmentVariable("WORKFLOWENGINE_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddJsonFile($"appsettings.Development.json", optional: true, reloadOnChange: true)
                .Build();
            connectionString = configuration.GetConnectionString("WorkflowEngine");
        }

        if (string.IsNullOrWhiteSpace(connectionString))
            connectionString = "Host=localhost;Port=5432;Database=TradeFlow;Username=TradeFlow;Password=TradeFlow";

        optionsBuilder
            .UseNpgsql(connectionString, npgsqlOptions =>
            {
                npgsqlOptions.MigrationsHistoryTable("__EFMigrationsHistory", Schemas.WorkflowEngine);
            })
            .UseSnakeCaseNamingConvention();

        return new WorkflowDbContext(
            optionsBuilder.Options,
            DesignTimeContext.Tenant,
            DesignTimeContext.User,
            DesignTimeContext.Dispatcher,
            DesignTimeContext.Services);
    }
}
