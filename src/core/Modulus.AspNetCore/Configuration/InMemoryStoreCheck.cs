namespace Modulus.AspNetCore.Configuration;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

/// <summary>What the in-memory store check does outside Development.</summary>
public enum InMemoryStoreCheckMode
{
    /// <summary>Logs one warning listing every in-memory store (the default).</summary>
    Warn,

    /// <summary>Refuses to start while a store not in <see cref="InMemoryStoreCheckOptions.Allow"/> is in memory.</summary>
    Fail,

    /// <summary>Checks nothing.</summary>
    Off,
}

/// <summary>Settings of the in-memory store check (section <c>Security:InMemoryStores</c>).</summary>
public sealed class InMemoryStoreCheckOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Security:InMemoryStores";

    /// <summary>What to do outside Development. Default <see cref="InMemoryStoreCheckMode.Warn"/>.</summary>
    public InMemoryStoreCheckMode Mode { get; set; } = InMemoryStoreCheckMode.Warn;

    /// <summary>
    /// Store contracts accepted in memory, by simple name (<c>ILocalizationStore</c>), e.g. on a deliberately
    /// single-node deployment or for a store seeded from code at every start.
    /// </summary>
    public string[] Allow { get; set; } = [];
}

/// <summary>Registers the in-memory store check.</summary>
public static class InMemoryStoreCheckExtensions
{
    /// <summary>
    /// Checks at startup, outside Development, which framework stores (permission grants, memberships, settings, audit,
    /// idempotency, notifications, delegation, ...) are still the per-process in-memory defaults. Their data is lost on
    /// restart and differs between replicas. Section <c>Security:InMemoryStores</c>: <c>Mode</c> <c>Warn</c> (default),
    /// <c>Fail</c> or <c>Off</c>, and <c>Allow</c>. <c>AddModulusSecurityGuard</c> calls it.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    public static IServiceCollection AddModulusInMemoryStoreCheck(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        if (services.Any(d => d.ServiceType == typeof(InMemoryStoreCheckRegistration)))
            return services;

        services.AddOptions<InMemoryStoreCheckOptions>().Bind(configuration.GetSection(InMemoryStoreCheckOptions.SectionName));
        // The collection is read when the host starts, so registrations made after this call count too.
        services.AddSingleton(new InMemoryStoreCheckRegistration(services));
        services.AddHostedService<InMemoryStoreCheck>();
        return services;
    }
}

internal sealed record InMemoryStoreCheckRegistration(IServiceCollection Services);

internal sealed partial class InMemoryStoreCheck(
    InMemoryStoreCheckRegistration registration,
    IServiceProvider services,
    Microsoft.Extensions.Options.IOptions<InMemoryStoreCheckOptions> options,
    ILogger<InMemoryStoreCheck> logger,
    IHostEnvironment? environment = null) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (settings.Mode == InMemoryStoreCheckMode.Off || environment?.IsDevelopment() == true)
            return Task.CompletedTask;

        var found = Find(registration.Services, services);
        if (found.Count == 0)
            return Task.CompletedTask;

        var refused = found.Where(f => !settings.Allow.Contains(f.Contract, StringComparer.Ordinal)).ToList();
        var list = string.Join(", ", found.Select(f => $"{f.Contract} ({f.Implementation})"));
        if (settings.Mode == InMemoryStoreCheckMode.Fail && refused.Count > 0)
        {
            throw new InvalidOperationException(
                "These stores keep their data in process memory (lost on restart, different on every replica): "
                + string.Join(", ", refused.Select(f => $"{f.Contract} ({f.Implementation})"))
                + $". Register durable stores, or list accepted ones in {InMemoryStoreCheckOptions.SectionName}:Allow.");
        }

        LogInMemoryStores(found.Count, list);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>The framework's in-memory implementations among the effective registrations (the last one per contract).</summary>
    internal static IReadOnlyList<InMemoryStore> Find(IServiceCollection registrations, IServiceProvider services)
    {
        var effective = registrations
            .Where(d => !d.IsKeyedService && !d.ServiceType.IsGenericTypeDefinition)
            .GroupBy(d => d.ServiceType)
            .Select(g => g.Last());

        var found = new List<InMemoryStore>();
        using var scope = services.CreateScope();
        foreach (var descriptor in effective)
        {
            var implementation = descriptor.ImplementationType
                ?? descriptor.ImplementationInstance?.GetType()
                ?? ResolveFactory(descriptor, scope.ServiceProvider);
            if (implementation is not null && IsFrameworkInMemory(implementation))
                found.Add(new InMemoryStore(descriptor.ServiceType.Name, implementation.Name));
        }

        return found.OrderBy(f => f.Contract, StringComparer.Ordinal).ToList();
    }

    // A factory hides its type; resolve only framework store contracts to see what it builds.
    private static Type? ResolveFactory(ServiceDescriptor descriptor, IServiceProvider services)
    {
        if (descriptor.ImplementationFactory is null
            || !IsFramework(descriptor.ServiceType)
            || !descriptor.ServiceType.Name.EndsWith("Store", StringComparison.Ordinal))
            return null;
        try
        {
            return services.GetService(descriptor.ServiceType)?.GetType();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static bool IsFrameworkInMemory(Type type)
        => type.Name.StartsWith("InMemory", StringComparison.Ordinal) && IsFramework(type);

    private static bool IsFramework(Type type)
        => type.Assembly.GetName().Name?.StartsWith("Modulus.", StringComparison.Ordinal) == true;

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "{Count} store(s) keep their data in process memory, lost on restart and different on every replica: {Stores}. "
            + "Register durable stores for production (Security:InMemoryStores:Mode = Fail refuses to start).")]
    private partial void LogInMemoryStores(int count, string stores);
}

/// <summary>A store contract served by an in-memory implementation.</summary>
internal sealed record InMemoryStore(string Contract, string Implementation);
