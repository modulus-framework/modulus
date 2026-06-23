using global::Hangfire;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulus.BackgroundJobs;

namespace Modulus.BackgroundJobs.Hangfire.Extensions;

public static class HangfireExtensions
{
    /// <summary>
    /// Registers Hangfire as the IJobScheduler implementation.
    /// Uses in-memory storage by default; configure SQL Server or Redis
    /// storage via GlobalConfiguration before calling this method.
    /// </summary>
    public static IServiceCollection AddHangfireJobs(
        this IServiceCollection services,
        Action<IGlobalConfiguration>? configureStorage = null)
    {
        var config = GlobalConfiguration.Configuration;
        configureStorage?.Invoke(config);

        services.AddHangfire(configuration => { });
        services.AddHangfireServer();
        services.AddScoped<IJobScheduler, HangfireJobScheduler>();

        return services;
    }

    /// <summary>
    /// Maps the Hangfire dashboard.
    /// </summary>
    public static WebApplication UseHangfireDashboard(
        this WebApplication app,
        string pathMatch = "/hangfire")
    {
        app.UseHangfireDashboard(pathMatch);
        return app;
    }
}
