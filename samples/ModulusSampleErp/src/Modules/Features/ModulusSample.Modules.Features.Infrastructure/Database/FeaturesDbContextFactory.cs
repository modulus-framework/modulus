using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Modulus.EntityFrameworkCore.Design;

namespace ModulusSample.Modules.Features.Infrastructure.Database;

internal sealed class FeaturesDbContextFactory : IDesignTimeDbContextFactory<FeaturesDbContext>
{
    private const string DefaultConnectionString =
        "Host=localhost;Port=5432;Database=modulussample_features;Username=postgres;Password=postgres";

    public FeaturesDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("FEATURES_CONNECTION")
                               ?? DefaultConnectionString;

        var optionsBuilder = new DbContextOptionsBuilder<FeaturesDbContext>();
        optionsBuilder
            .UseNpgsql(connectionString, npgsqlOptions =>
                npgsqlOptions.MigrationsHistoryTable(
                    "__EFMigrationsHistory", Schemas.Features))
            .UseSnakeCaseNamingConvention();

        return new FeaturesDbContext(
            optionsBuilder.Options,
            DesignTimeContext.Tenant,
            DesignTimeContext.User,
            DesignTimeContext.Dispatcher,
            DesignTimeContext.Services);
    }
}