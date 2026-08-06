using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Modulus.EntityFrameworkCore.Design;

namespace ModulusSample.Modules.Settings.Infrastructure.Database;

internal sealed class SettingsDbContextFactory : IDesignTimeDbContextFactory<SettingsDbContext>
{
    private const string DefaultConnectionString =
        "Host=localhost;Port=5432;Database=modulussample;Username=postgres;Password=postgres";

    public SettingsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("SETTINGS_CONNECTION")
                               ?? DefaultConnectionString;

        var optionsBuilder = new DbContextOptionsBuilder<SettingsDbContext>();
        optionsBuilder
            .UseNpgsql(connectionString, npgsqlOptions =>
                npgsqlOptions.MigrationsHistoryTable(
                    "__EFMigrationsHistory", Schemas.Settings))
            .UseSnakeCaseNamingConvention();

        return new SettingsDbContext(
            optionsBuilder.Options,
            DesignTimeContext.Tenant,
            DesignTimeContext.User,
            DesignTimeContext.Dispatcher,
            DesignTimeContext.Services);
    }
}