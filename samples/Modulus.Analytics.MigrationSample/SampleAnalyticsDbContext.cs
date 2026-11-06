namespace Modulus.Analytics.MigrationSample;

using Modulus.Analytics.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// The application's analytics store. It derives from <see cref="ModulusAnalyticsDbContext"/>, so the migrations for the analytics tables
/// live in the application, and it is registered with <c>AddModulusAnalyticsStore&lt;SampleAnalyticsDbContext&gt;</c>.
/// </summary>
public sealed class SampleAnalyticsDbContext(DbContextOptions<SampleAnalyticsDbContext> options) : ModulusAnalyticsDbContext(options)
{
}
