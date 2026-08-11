using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Modulus.EntityFrameworkCore.Design;

namespace ModulusSample.Modules.Purchasing.Infrastructure.Database;

public sealed class PurchasingDbContextFactory : IDesignTimeDbContextFactory<PurchasingDbContext>
{
    public PurchasingDbContext CreateDbContext(string[] args)
    {
        var connectionString = "Host=localhost;Database=modulus_sample_purchasing;Username=postgres;Password=postgres";
        var optionsBuilder = new DbContextOptionsBuilder<PurchasingDbContext>()
            .UseNpgsql(connectionString);

        return new PurchasingDbContext(
            optionsBuilder.Options,
            DesignTimeContext.Tenant,
            DesignTimeContext.User,
            DesignTimeContext.Dispatcher,
            DesignTimeContext.Services);
    }
}
