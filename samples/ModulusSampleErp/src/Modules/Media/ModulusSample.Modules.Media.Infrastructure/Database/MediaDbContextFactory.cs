using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Migrations;
using Modulus.EntityFrameworkCore.Design;
using ModulusSample.Modules.Media.Infrastructure.Database;

namespace ModulusSample.Modules.Media.Infrastructure.Database;

/// <summary>
/// Design-time factory used by the EF Core tools (<c>dotnet ef</c> /
/// <c>modulus migrate</c>) to construct <see cref="MediaDbContext"/>
/// without the application's DI container or an HTTP request. At runtime the app
/// still builds the context through DI (see <see cref="MediaModule"/>);
/// this type is only used when scaffolding or applying migrations.
/// </summary>
/// <remarks>
/// The connection string is read from the <c>MEDIA_CONNECTION</c>
/// environment variable when set — so CI/CD can point migrations at the real
/// database — otherwise it falls back to the module's default design-time
/// connection string. The tenant/user/dispatcher arguments are design-time stubs
/// from <see cref="DesignTimeContext"/>: migrations only build the model, never
/// live request state.
/// </remarks>
public sealed class MediaDbContextFactory
    : IDesignTimeDbContextFactory<MediaDbContext>
{
    private const string DefaultConnectionString =
        "Host=localhost;Port=5432;Database=modulussample;Username=ModulusSample;Password=ModulusSample";

    public MediaDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("MEDIA_CONNECTION")
            ?? DefaultConnectionString;

        var options = new DbContextOptionsBuilder<MediaDbContext>()
            .UseNpgsql(connectionString, npgsqlOptions =>
                npgsqlOptions.MigrationsHistoryTable(
                    HistoryRepository.DefaultTableName, Schemas.Media))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new MediaDbContext(
            options,
            DesignTimeContext.Tenant,
            DesignTimeContext.User,
            DesignTimeContext.Dispatcher,
            DesignTimeContext.Services);
    }
}
