using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Modulus.EntityFrameworkCore.Design;
using TradeFlow.Modules.Customs.Domain.Constants;

namespace TradeFlow.Modules.Customs.Infrastructure.Database;

public sealed class CustomsDbContextFactory : IDesignTimeDbContextFactory<CustomsDbContext>
{
    public CustomsDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<CustomsDbContext>();

        string? connectionString = Environment.GetEnvironmentVariable("CUSTOMS_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: true)
                .Build();

            connectionString = configuration.GetConnectionString("Customs");
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
                    Schemas.Customs);
                npgsqlOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
            })
            .UseSnakeCaseNamingConvention();

        return new CustomsDbContext(
            optionsBuilder.Options,
            DesignTimeContext.Tenant,
            DesignTimeContext.User,
            DesignTimeContext.Dispatcher,
            DesignTimeContext.Services);
    }
}