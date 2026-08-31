using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Modulus.EntityFrameworkCore.Design;

namespace TradeFlow.Modules.Notifications.Infrastructure.Database;

internal sealed class NotificationsDbContextFactory : IDesignTimeDbContextFactory<NotificationsDbContext>
{
    private const string DefaultConnectionString =
        "Host=localhost;Port=5432;Database=TradeFlow;Username=postgres;Password=postgres";

    public NotificationsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("NOTIFICATIONS_CONNECTION")
                               ?? DefaultConnectionString;

        var optionsBuilder = new DbContextOptionsBuilder<NotificationsDbContext>();
        optionsBuilder
            .UseNpgsql(connectionString, npgsqlOptions =>
                npgsqlOptions.MigrationsHistoryTable(
                    "__EFMigrationsHistory", Schemas.Notifications))
            .UseSnakeCaseNamingConvention();

        return new NotificationsDbContext(
            optionsBuilder.Options,
            DesignTimeContext.Tenant,
            DesignTimeContext.User,
            DesignTimeContext.Dispatcher,
            DesignTimeContext.Services);
    }
}
