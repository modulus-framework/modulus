using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Modulus.EntityFrameworkCore.Design;

namespace ModulusSample.Modules.Partners.Infrastructure.Database;

public sealed class PartnersDbContextFactory : IDesignTimeDbContextFactory<PartnersDbContext>
{
    public PartnersDbContext CreateDbContext(string[] args)
    {
        var connectionString = "Host=localhost;Database=modulus_sample_partners;Username=postgres;Password=postgres";
        var optionsBuilder = new DbContextOptionsBuilder<PartnersDbContext>()
            .UseNpgsql(connectionString);

        return new PartnersDbContext(
            optionsBuilder.Options,
            DesignTimeContext.Tenant,
            DesignTimeContext.User,
            DesignTimeContext.Dispatcher,
            DesignTimeContext.Services);
    }
}
