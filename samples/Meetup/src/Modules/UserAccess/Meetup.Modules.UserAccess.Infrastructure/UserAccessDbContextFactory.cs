using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Modulus.EntityFrameworkCore;
using Modulus.EntityFrameworkCore.Design;
using Meetup.Modules.UserAccess.Application;
using Meetup.Modules.UserAccess.Domain;

namespace Meetup.Modules.UserAccess.Infrastructure;

public sealed class UserAccessDbContextFactory : IDesignTimeDbContextFactory<UserAccessDbContext>
{
    public UserAccessDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<UserAccessDbContext>();

        string? connectionString = Environment.GetEnvironmentVariable("USERACCESS_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: true)
                .Build();

            connectionString = configuration.GetConnectionString("UserAccess");
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = "Host=localhost;Port=5432;Database=meetup_useraccess;Username=meetup;Password=meetup";
        }

        optionsBuilder.UseNpgsql(connectionString);

        return new UserAccessDbContext(
            optionsBuilder.Options,
            DesignTimeContext.Tenant,
            DesignTimeContext.User,
            DesignTimeContext.Dispatcher,
            DesignTimeContext.Services);
    }
}
