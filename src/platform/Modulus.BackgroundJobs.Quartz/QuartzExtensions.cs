using Microsoft.Extensions.DependencyInjection;
using Quartz;

namespace Modulus.BackgroundJobs.Quartz;

/// <summary>
/// Registration extensions for Quartz.NET-backed job scheduling.
/// Replaces the in-memory ChannelJobQueue with Quartz.NET, which provides
/// durability via a configurable job store and built-in clustering support.
/// </summary>
public static class QuartzExtensions
{
    /// <summary>
    /// Registers Quartz.NET as the background job scheduler.
    /// <para>
    /// By default, this uses an in-memory store suitable for dev/test. For
    /// production multi-replica deployments, applications should call
    /// UseAdoNetStore() on the configurator to persist jobs to a database
    /// and enable clustering.
    /// </para>
    /// </summary>
    /// <param name="services">Service collection to register with.</param>
    /// <param name="configureQuartz">Optional action to customize Quartz configuration.</param>
    public static IServiceCollection AddQuartzJobScheduler(
        this IServiceCollection services,
        Action<IServiceCollectionQuartzConfigurator>? configureQuartz = null)
    {
        services.AddQuartz(q =>
        {
            q.UseInMemoryStore();
            q.UseDefaultThreadPool(tp => tp.MaxConcurrency = 10);

            configureQuartz?.Invoke(q);
        });

        services.AddHostedService<QuartzHostedService>();
        services.AddScoped<IJobScheduler, QuartzJobScheduler>();

        return services;
    }
}
