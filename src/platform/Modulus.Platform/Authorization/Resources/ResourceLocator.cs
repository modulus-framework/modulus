namespace Modulus.Authorization.Resources;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>
/// Loads a record by a public type name and id so per-record authorization questions can be asked over HTTP
/// ("what may I do to order 42?"). The app registers one loader per exposed type with <c>AddResourceLocator</c>;
/// a type with no loader is not exposed. The loader runs in the request scope, so its queries stay tenant-filtered.
/// </summary>
public interface IResourceLocator
{
    /// <summary>The record, or <see langword="null"/> for an unknown type or id (indistinguishable, so nothing leaks).</summary>
    ValueTask<object?> FindAsync(string resourceType, string id, CancellationToken ct = default);
}

/// <summary>One registered loader (see <see cref="ResourceLocatorExtensions.AddResourceLocator"/>).</summary>
/// <param name="Name">The public type name used in URLs.</param>
/// <param name="Load">Loads the record from the request scope.</param>
public sealed record ResourceLocatorRegistration(
    string Name, Func<IServiceProvider, string, CancellationToken, ValueTask<object?>> Load);

internal sealed class ResourceLocator(IServiceProvider services, IEnumerable<ResourceLocatorRegistration> registrations)
    : IResourceLocator
{
    public ValueTask<object?> FindAsync(string resourceType, string id, CancellationToken ct = default)
    {
        var registration = registrations.LastOrDefault(r => string.Equals(r.Name, resourceType, StringComparison.OrdinalIgnoreCase));
        return registration is null ? ValueTask.FromResult<object?>(null) : registration.Load(services, id, ct);
    }
}

/// <summary>Registration of <see cref="IResourceLocator"/> loaders.</summary>
public static class ResourceLocatorExtensions
{
    /// <summary>Exposes records of the public type name <paramref name="name"/> to the resource authorization endpoints.</summary>
    public static IServiceCollection AddResourceLocator(
        this IServiceCollection services,
        string name,
        Func<IServiceProvider, string, CancellationToken, ValueTask<object?>> load)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(load);

        services.AddSingleton(new ResourceLocatorRegistration(name, load));
        services.TryAddScoped<IResourceLocator, ResourceLocator>();
        return services;
    }
}
