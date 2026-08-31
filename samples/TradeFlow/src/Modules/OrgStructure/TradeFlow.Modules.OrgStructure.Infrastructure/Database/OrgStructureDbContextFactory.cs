using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using TradeFlow.Modules.OrgStructure.Domain.Constants;
using Modulus.EntityFrameworkCore.Design;

namespace TradeFlow.Modules.OrgStructure.Infrastructure.Database;

public sealed class OrgStructureDbContextFactory : IDesignTimeDbContextFactory<OrgStructureDbContext>
{
    public OrgStructureDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<OrgStructureDbContext>();

        string? connectionString = Environment.GetEnvironmentVariable("ORGSTRUCTURE_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddJsonFile($"appsettings.Development.json", optional: true, reloadOnChange: true)
                .Build();
            connectionString = configuration.GetConnectionString("OrgStructure");
        }

        if (string.IsNullOrWhiteSpace(connectionString))
            connectionString = "Host=localhost;Port=5432;Database=TradeFlow;Username=TradeFlow;Password=TradeFlow";

        optionsBuilder
            .UseNpgsql(connectionString, npgsqlOptions =>
            {
                npgsqlOptions.MigrationsHistoryTable("__EFMigrationsHistory", Schemas.OrgStructure);
            })
            .UseSnakeCaseNamingConvention();

        return new OrgStructureDbContext(
            optionsBuilder.Options,
            DesignTimeContext.Tenant,
            DesignTimeContext.User,
            DesignTimeContext.Dispatcher,
            DesignTimeContext.Services);
    }
}
