using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;

namespace ModulusSample.Modules.Inventory.Infrastructure;

public sealed class InventoryModule : ModulusModule
{
    public override void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException("Database connection string not configured");

        services.AddDbContext<Database.InventoryDbContext>(options =>
            options.UseNpgsql(connectionString));
    }
}
