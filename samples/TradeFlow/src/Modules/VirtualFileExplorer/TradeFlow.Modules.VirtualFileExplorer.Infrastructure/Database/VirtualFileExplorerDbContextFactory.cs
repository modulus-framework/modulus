using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Modulus.EntityFrameworkCore.Design;

namespace TradeFlow.Modules.VirtualFileExplorer.Infrastructure.Database;

internal sealed class VirtualFileExplorerDbContextFactory : IDesignTimeDbContextFactory<VirtualFileExplorerDbContext>
{
    private const string DefaultConnectionString =
        "Host=localhost;Port=5432;Database=TradeFlow;Username=TradeFlow;Password=TradeFlow";

    public VirtualFileExplorerDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("VIRTUALFILEEXPLORER_CONNECTION")
                               ?? DefaultConnectionString;

        var optionsBuilder = new DbContextOptionsBuilder<VirtualFileExplorerDbContext>();
        optionsBuilder
            .UseNpgsql(connectionString, npgsqlOptions =>
                npgsqlOptions.MigrationsHistoryTable(
                    "__EFMigrationsHistory", Schemas.VirtualFileExplorer))
            .UseSnakeCaseNamingConvention();

        return new VirtualFileExplorerDbContext(
            optionsBuilder.Options,
            DesignTimeContext.Tenant,
            DesignTimeContext.User,
            DesignTimeContext.Dispatcher,
            DesignTimeContext.Services);
    }
}
