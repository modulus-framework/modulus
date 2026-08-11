using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Modulus.EntityFrameworkCore.Design;

namespace ModulusSample.Modules.Sales.Infrastructure.Database;

public sealed class SalesDbContextFactory : IDesignTimeDbContextFactory<SalesDbContext>
{
    public SalesDbContext CreateDbContext(string[] args)
    {
        var connectionString = "Host=localhost;Database=modulus_sample_sales;Username=postgres;Password=postgres";
        var optionsBuilder = new DbContextOptionsBuilder<SalesDbContext>()
            .UseNpgsql(connectionString);

        return new SalesDbContext(
            optionsBuilder.Options,
            DesignTimeContext.Tenant,
            DesignTimeContext.User,
            DesignTimeContext.Dispatcher,
            DesignTimeContext.Services);
    }
}
