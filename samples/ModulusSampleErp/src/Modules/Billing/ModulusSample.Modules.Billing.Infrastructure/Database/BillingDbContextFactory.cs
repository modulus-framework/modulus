using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Modulus.Core.Domain;
using ModulusSample.Shared.Infrastructure.Extensions;

namespace ModulusSample.Modules.Billing.Infrastructure.Database;

public sealed class BillingDbContextFactory : IDesignTimeDbContextFactory<BillingDbContext>
{
    public BillingDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<BillingDbContext>();
        const string connectionString = "Host=localhost;Port=5432;Database=ModulusSample;Username=ModulusSample;Password=ModulusSample";
        optionsBuilder.UseNpgsql(connectionString);

        return new BillingDbContext(
            optionsBuilder.Options,
            DesignTimeContext.Tenant,
            DesignTimeContext.User,
            DesignTimeContext.Dispatcher,
            DesignTimeContext.Services);
    }
}
