using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Modulus.EntityFrameworkCore.Design;

namespace ModulusSample.Modules.Inventory.Infrastructure.Database;

public sealed class InventoryDbContextFactory : IDesignTimeDbContextFactory<InventoryDbContext>
{
    public InventoryDbContext CreateDbContext(string[] args)
    {
        var connectionString = "Host=localhost;Database=modulus_sample_inventory;Username=postgres;Password=postgres";
        var optionsBuilder = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseNpgsql(connectionString);

        return new InventoryDbContext(
            optionsBuilder.Options,
            DesignTimeContext.Tenant,
            DesignTimeContext.User,
            DesignTimeContext.Dispatcher,
            DesignTimeContext.Services);
    }
}
