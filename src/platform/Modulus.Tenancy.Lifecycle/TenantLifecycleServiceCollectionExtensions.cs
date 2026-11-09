namespace Modulus.Tenancy.Lifecycle;

using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;
using Modulus.Authorization.Extensions;

/// <summary>Registers the tenant data lifecycle. Call once; the worker starts with the host.</summary>
public static class TenantLifecycleServiceCollectionExtensions
{
    /// <summary>The permission that lets a company's administrators request and download its own export.</summary>
    public const string ExportPermission = "tenant-data:export";

    /// <summary>
    /// Binds <c>TenantLifecycle</c>, registers the provider, the worker and the in-memory store (replace it with
    /// <c>AddTenantLifecycleStore</c>'s EF store in production). Modules add their handlers in <paramref name="configure"/>.
    /// Requires file storage (<c>IFileStorage</c>) and multi-tenancy (<c>ITenantStore</c>).
    /// </summary>
    public static IServiceCollection AddTenantLifecycle(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<TenantLifecycleBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<TenantLifecycleOptions>()
            .Bind(configuration.GetSection(TenantLifecycleOptions.SectionName));
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ITenantLifecycleStore, InMemoryTenantLifecycleStore>();

        var builder = new TenantLifecycleBuilder(services);
        configure?.Invoke(builder);

        services.TryAddSingleton(sp =>
        {
            using var scope = sp.CreateScope();
            var handlers = builder.HandlerTypes
                .Select(type => (type, (ITenantDataHandler)scope.ServiceProvider.GetRequiredService(type)))
                .ToList();
            return TenantDataRegistry.Build(handlers);
        });
        // Scoped: the provider uses the store, which an EF store implements with a scoped DbContext.
        services.TryAddScoped<ITenantLifecycleProvider, TenantLifecycleProvider>();
        services.AddHostedService<TenantLifecycleWorker>();
        services.TryAddScoped<TenantRetentionJob>();
        services.AddHostedService<TenantRetentionScheduleService>();

        // The export endpoints: a company's administrator only, through the permission system (or a "permission" claim
        // when the host has no grant store). Offboarding is an operator action in code, not an HTTP endpoint.
        services.AddAuthorization(o => o.AddPolicy(ExportPermission, p => p
            .RequireAuthenticatedUser()
            .RequireClaim("permission", ExportPermission)));
        services.AddPermissions("Modulus.Tenancy.Lifecycle", registry => registry.Add(
            ExportPermission,
            "Request and download the company's data export (encrypted archive, expires after the configured lifetime)."));
        return services;
    }

    /// <summary>
    /// Stores lifecycle operations, holds and pending deletions in the host database (<typeparamref name="TStore"/>, for example the
    /// EF store). Replaces the in-memory store.
    /// </summary>
    public static IServiceCollection AddTenantLifecycleStore<TStore>(this IServiceCollection services)
        where TStore : class, ITenantLifecycleStore
    {
        ArgumentNullException.ThrowIfNull(services);
        services.RemoveAll<ITenantLifecycleStore>();
        services.AddScoped<ITenantLifecycleStore, TStore>();
        return services;
    }
}

/// <summary>Collects a module's tenant-data handlers and offboarding steps.</summary>
public sealed class TenantLifecycleBuilder
{
    private readonly IServiceCollection _services;
    private readonly List<Type> _handlers = [];

    internal TenantLifecycleBuilder(IServiceCollection services) => _services = services;

    internal IReadOnlyList<Type> HandlerTypes => _handlers;

    /// <summary>The service collection, for a module's lifecycle extension that has to register or wrap other services.</summary>
    public IServiceCollection Services => _services;

    /// <summary>Registers a module's handler. It is resolved in the company's tenant context for each operation.</summary>
    public TenantLifecycleBuilder AddHandler<THandler>() where THandler : class, ITenantDataHandler
    {
        _services.TryAddScoped<THandler>();
        if (!_handlers.Contains(typeof(THandler)))
            _handlers.Add(typeof(THandler));
        return this;
    }

    /// <summary>Registers an offboarding step (for example stopping a module's scheduled jobs for the company).</summary>
    public TenantLifecycleBuilder AddOffboardingStep<TStep>() where TStep : class, ITenantOffboardingStep
    {
        _services.AddScoped<ITenantOffboardingStep, TStep>();
        return this;
    }
}
