namespace Modulus.Analytics.MigrationSample;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

/// <summary>Lets <c>dotnet ef</c> build the context at design time. The connection string is a placeholder; the real one comes from configuration.</summary>
public sealed class SampleAnalyticsDbContextFactory : IDesignTimeDbContextFactory<SampleAnalyticsDbContext>
{
    /// <inheritdoc />
    public SampleAnalyticsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<SampleAnalyticsDbContext>()
            .UseNpgsql("Host=localhost;Database=app;Username=app;Password=change-me")
            .Options;
        return new SampleAnalyticsDbContext(options);
    }
}
