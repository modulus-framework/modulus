namespace Modulus.Media;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Modulus.Authorization.Extensions;
using Modulus.Core.Abstractions;

public static class MediaExtensions
{
    /// <summary>
    /// Registers the media library: <see cref="IMediaLibrary"/> and, unless the host supplied one first, the in-memory <see cref="IMediaStore"/>.
    /// It needs an <see cref="Modulus.Storage.IFileStorage"/> (the storage extensions register one). Registers <c>media:read</c> and
    /// <c>media:manage</c> so they can be granted; they are enforced once <c>AddModulusAuthorization</c> is in place.
    /// Limits bind from the <c>Media</c> section when the host configures it.
    /// </summary>
    public static IServiceCollection AddModulusMedia(this IServiceCollection services)
    {
        services.AddOptions<MediaOptions>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IMediaStore, InMemoryMediaStore>();
        services.TryAddScoped<IMediaLibrary, MediaLibrary>();
        services.AddPermissions("Modulus.Media", registry =>
        {
            registry.Add(MediaPermissions.Read, "List and download the media files of the current company.", null, PermissionSensitivity.Normal);
            registry.Add(MediaPermissions.Manage, "Upload and delete the media files of the current company.", null, PermissionSensitivity.Sensitive);
        });
        return services;
    }
}
