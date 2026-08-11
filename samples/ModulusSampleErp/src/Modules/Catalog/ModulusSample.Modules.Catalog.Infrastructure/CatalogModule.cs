using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;

namespace ModulusSample.Modules.Catalog.Infrastructure;

public sealed class CatalogModule : ModulusModule
{
    public override void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // Wire up EF Core DbContext
        var connectionString = configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException("Database connection string not configured");

        services.AddDbContext<Database.CatalogDbContext>(options =>
            options.UseNpgsql(connectionString));
    }
}
