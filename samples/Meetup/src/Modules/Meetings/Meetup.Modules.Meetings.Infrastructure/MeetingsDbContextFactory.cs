using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Modulus.EntityFrameworkCore;
using Modulus.EntityFrameworkCore.Design;
using Meetup.Modules.Meetings.Application;
using Meetup.Modules.Meetings.Domain;

namespace Meetup.Modules.Meetings.Infrastructure;

public sealed class MeetingsDbContextFactory : IDesignTimeDbContextFactory<MeetingsDbContext>
{
    public MeetingsDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<MeetingsDbContext>();

        string? connectionString = Environment.GetEnvironmentVariable("MEETINGS_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: true)
                .Build();

            connectionString = configuration.GetConnectionString("Meetings");
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = "Host=localhost;Port=5432;Database=meetup_meetings;Username=meetup;Password=meetup";
        }

        optionsBuilder.UseNpgsql(connectionString);

        return new MeetingsDbContext(
            optionsBuilder.Options,
            DesignTimeContext.Tenant,
            DesignTimeContext.User,
            DesignTimeContext.Dispatcher,
            DesignTimeContext.Services);
    }
}
