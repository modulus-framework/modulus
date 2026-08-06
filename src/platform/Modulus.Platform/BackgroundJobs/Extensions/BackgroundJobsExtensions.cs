namespace Modulus.BackgroundJobs.Extensions;

using System.Reflection;

public static class BackgroundJobsExtensions
{
    public static IServiceCollection AddBackgroundJobs(
        this IServiceCollection services,
        params Assembly[] assemblies)
    {
        services.AddSingleton<ChannelJobQueue>();
        services.AddSingleton<IJobScheduler>(
            sp => sp.GetRequiredService<ChannelJobQueue>());
        services.AddHostedService(
            sp => sp.GetRequiredService<ChannelJobQueue>());

        foreach (var assembly in assemblies)
        {
            foreach (var type in assembly.GetTypes()
                .Where(t => t is { IsAbstract: false, IsInterface: false }))
            {
                foreach (var iface in type.GetInterfaces()
                    .Where(i => i.IsGenericType
                        && i.GetGenericTypeDefinition() == typeof(IBackgroundJob<>)))
                {
                    // Register both the concrete type and the closed generic
                    // IBackgroundJob<TArgs> so the worker can resolve via the
                    // interface without knowing the concrete type.
                    services.AddScoped(type);
                    services.AddScoped(iface, type);
                }
            }
        }

        return services;
    }
}
