using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Modulus.EntityFrameworkCore.Design;

namespace ModulusSample.Modules.Catalog.Infrastructure.Database;

public sealed class CatalogDbContextFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    public CatalogDbContext CreateDbContext(string[] args)
    {
        var connectionString = "Host=localhost;Database=modulus_sample_catalog;Username=postgres;Password=postgres";
        var optionsBuilder = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(connectionString);

        return new CatalogDbContext(
            optionsBuilder.Options,
            DesignTimeContext.Tenant,
            DesignTimeContext.User,
            DesignTimeContext.Dispatcher,
            DesignTimeContext.Services);
    }
}
