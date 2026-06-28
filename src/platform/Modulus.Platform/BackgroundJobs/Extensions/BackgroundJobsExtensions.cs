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
            foreach (var type in assembly.GetTypes()
                .Where(t => t is { IsAbstract: false, IsInterface: false }
                    && t.GetInterfaces().Any(i => i.IsGenericType
                        && i.GetGenericTypeDefinition() == typeof(IBackgroundJob<>))))
                services.AddScoped(type);

        return services;
    }
}