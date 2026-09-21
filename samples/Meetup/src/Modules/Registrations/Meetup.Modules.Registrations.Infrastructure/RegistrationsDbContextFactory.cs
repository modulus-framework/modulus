using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Modulus.EntityFrameworkCore;
using Modulus.EntityFrameworkCore.Design;
using Meetup.Modules.Registrations.Application;
using Meetup.Modules.Registrations.Domain;

namespace Meetup.Modules.Registrations.Infrastructure;

public sealed class RegistrationsDbContextFactory : IDesignTimeDbContextFactory<RegistrationsDbContext>
{
    public RegistrationsDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<RegistrationsDbContext>();

        string? connectionString = Environment.GetEnvironmentVariable("REGISTRATIONS_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: true)
                .Build();

            connectionString = configuration.GetConnectionString("Registrations");
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = "Host=localhost;Port=5432;Database=meetup_registrations;Username=meetup;Password=meetup";
        }

        optionsBuilder.UseNpgsql(connectionString);

        return new RegistrationsDbContext(
            optionsBuilder.Options,
            DesignTimeContext.Tenant,
            DesignTimeContext.User,
            DesignTimeContext.Dispatcher,
            DesignTimeContext.Services);
    }
}
