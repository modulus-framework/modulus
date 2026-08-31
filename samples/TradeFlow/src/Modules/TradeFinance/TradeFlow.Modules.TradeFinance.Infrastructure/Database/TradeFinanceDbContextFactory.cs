using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Modulus.EntityFrameworkCore.Design;
using TradeFlow.Modules.TradeFinance.Domain.Constants;

namespace TradeFlow.Modules.TradeFinance.Infrastructure.Database;

public sealed class TradeFinanceDbContextFactory : IDesignTimeDbContextFactory<TradeFinanceDbContext>
{
    public TradeFinanceDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<TradeFinanceDbContext>();

        string? connectionString = Environment.GetEnvironmentVariable("TRADEFINANCE_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: true)
                .Build();

            connectionString = configuration.GetConnectionString("TradeFinance");
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = "Host=localhost;Port=5432;Database=TradeFlow;Username=TradeFlow;Password=TradeFlow";
        }

        optionsBuilder
            .UseNpgsql(connectionString, npgsqlOptions =>
            {
                npgsqlOptions.MigrationsHistoryTable(
                    "__EFMigrationsHistory",
                    Schemas.TradeFinance);
                npgsqlOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
            })
            .UseSnakeCaseNamingConvention();

        return new TradeFinanceDbContext(
            optionsBuilder.Options,
            DesignTimeContext.Tenant,
            DesignTimeContext.User,
            DesignTimeContext.Dispatcher,
            DesignTimeContext.Services);
    }
}