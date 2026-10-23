using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Modulus.Authorization.Sharing;

public static class RecordSharingExtensions
{
    /// <summary>Registers record sharing (A8-1). The in-memory store is the default; register a durable <see cref="IRecordShareStore"/> first to replace it.</summary>
    public static IServiceCollection AddRecordSharing(this IServiceCollection services, Action<RecordShareOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var optionsBuilder = services.AddOptions<RecordShareOptions>();
        if (configure is not null)
            optionsBuilder.Configure(configure);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IRecordShareStore, InMemoryRecordShareStore>();
        services.TryAddScoped<RecordShares>();
        return services;
    }
}
