using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using ModulusSample.Modules.Purchasing.Infrastructure.Database;

namespace ModulusSample.Modules.Purchasing.Infrastructure;

public sealed class PurchasingModule : ModulusModule
{
    public override void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException("Database connection string not configured");

        services.AddDbContext<PurchasingDbContext>(options =>
            options.UseNpgsql(connectionString));
    }
}
