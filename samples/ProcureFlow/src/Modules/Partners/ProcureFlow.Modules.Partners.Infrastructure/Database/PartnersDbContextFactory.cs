using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Modulus.EntityFrameworkCore.Design;

namespace ModulusSample.Modules.Partners.Infrastructure.Database;

public sealed class PartnersDbContextFactory : IDesignTimeDbContextFactory<PartnersDbContext>
{
    private const string DefaultConnectionString =
        "Host=localhost;Port=5432;Database=ModulusSample;Username=ModulusSample;Password=ModulusSample";

    public PartnersDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("PARTNERS_CONNECTION")
                               ?? DefaultConnectionString;

        var optionsBuilder = new DbContextOptionsBuilder<PartnersDbContext>();
        optionsBuilder
            .UseNpgsql(connectionString, npgsqlOptions =>
                npgsqlOptions.MigrationsHistoryTable(
                    "__EFMigrationsHistory", PartnersDbContext.SchemaName))
            .UseSnakeCaseNamingConvention();

        return new PartnersDbContext(
            optionsBuilder.Options,
            DesignTimeContext.Tenant,
            DesignTimeContext.User,
            DesignTimeContext.Dispatcher,
            DesignTimeContext.Services);
    }
}
