using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Modulus.BackgroundJobs.Quartz.Tests;

/// <summary>
/// Quartz caches the first logger factory it sees for the whole process. Tests here build and dispose many hosts, so
/// a factory from <c>AddLogging()</c> is soon disposed and later scheduler calls fail. Every test host therefore uses the
/// never-disposed null factory: the tests assert on behavior, not on log output.
/// </summary>
internal static class QuartzTestServices
{
    internal static IServiceCollection AddSilentLogging(this IServiceCollection services)
    {
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        return services;
    }
}
