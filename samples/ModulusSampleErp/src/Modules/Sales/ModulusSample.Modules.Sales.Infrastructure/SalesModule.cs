using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;

namespace ModulusSample.Modules.Sales.Infrastructure;

public sealed class SalesModule : ModulusModule
{
    public override void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException("Database connection string not configured");

        services.AddDbContext<Database.SalesDbContext>(options =>
            options.UseNpgsql(connectionString));
    }
}
