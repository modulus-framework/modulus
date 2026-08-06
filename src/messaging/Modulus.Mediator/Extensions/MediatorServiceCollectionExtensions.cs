using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Modulus.Mediator.Extensions;

using System.Reflection;
using Modulus.Core.Abstractions;
using Modulus.Core.Null;
using Modulus.Mediator.Abstractions;
using Modulus.Mediator.Abstractions.Attributes;
using Modulus.Mediator.Behaviors;

public static class MediatorServiceCollectionExtensions
{
    public static IServiceCollection AddMediator(
        this IServiceCollection services,
        Action<MediatorOptions>? configure = null)
    {
        var opts = new MediatorOptions();
        configure?.Invoke(opts);

        services.AddScoped<IMediator, Mediator>();

        // Carries the transaction-scoping policy to TransactionBehavior.
        services.AddSingleton(new TransactionRuntimeOptions(opts.TransactionMode));

        // Ensure the pipeline behaviors' dependencies are always available.
        // TryAdd never overwrites a registration the host already made, so a
        // real Identity module (ICurrentUser) or caching configuration
        // (IMemoryCache) takes precedence over these fail-safe defaults.
        services.TryAddSingleton<IMemoryCache, MemoryCache>();
        services.TryAddScoped<ICurrentUser, NullCurrentUser>();
        services.TryAddScoped<IFeatureGate, NullFeatureGate>();

        // Register handlers from specified assemblies
        services.AddMediatorHandlers(opts.Assemblies.ToArray());

        // Register behaviors in order (first registered = outermost)
        if (opts.EnableLogging)
            services.AddScoped(typeof(IPipelineBehavior<,>),
                typeof(LoggingBehavior<,>));

        if (opts.EnableValidation)
            services.AddScoped(typeof(IPipelineBehavior<,>),
                typeof(ValidationBehavior<,>));

        if (opts.EnableAuthorization)
        {
            // Feature gate before the permission check: availability is decided ahead of
            // capability, since a feature disabled for the tenant is inaccessible to
            // everyone regardless of what they may do (blueprint §5.11, §14).
            services.AddScoped(typeof(IPipelineBehavior<,>),
                typeof(FeatureGateBehavior<,>));
            services.AddScoped(typeof(IPipelineBehavior<,>),
                typeof(AuthorizationBehavior<,>));
        }

        if (opts.EnableCaching)
            services.AddScoped(typeof(IPipelineBehavior<,>),
                typeof(CachingBehavior<,>));

        if (opts.EnableTransaction)
            services.AddScoped(typeof(IPipelineBehavior<,>),
                typeof(TransactionBehavior<,>));

        return services;
    }

    /// <summary>
    /// Registers command/query handlers from the given assemblies **without**
    /// touching the pipeline behaviors.  Use this from a module's
    /// <c>ConfigureServices</c> to contribute its handlers; call
    /// <see cref="AddMediator"/> once in the host to set up the behaviors.
    /// </summary>
    public static IServiceCollection AddMediatorHandlers(
        this IServiceCollection services, params Assembly[] assemblies)
    {
        foreach (var assembly in assemblies)
            RegisterHandlers(services, assembly);
        return services;
    }

    private static void RegisterHandlers(
        IServiceCollection services, Assembly assembly)
    {
        var handlerInterfaces = new[]
        {
            typeof(ICommandHandler<,>),
            typeof(IQueryHandler<,>),
        };

        foreach (var type in assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false }))
        {
            foreach (var iface in type.GetInterfaces()
                .Where(i => i.IsGenericType
                    && handlerInterfaces.Contains(i.GetGenericTypeDefinition())))
            {
                services.AddScoped(iface, type);
            }
        }
    }
}

public sealed class MediatorOptions
{
    public List<Assembly> Assemblies { get; } = [];
    public bool EnableLogging { get; set; } = true;
    public bool EnableValidation { get; set; } = true;
    public bool EnableAuthorization { get; set; } = true;
    public bool EnableCaching { get; set; } = true;
    public bool EnableTransaction { get; set; } = true;

    /// <summary>
    /// How <c>TransactionBehavior</c> chooses which <c>DbContext</c>s to wrap when
    /// a command carries no <see cref="Abstractions.Attributes.TransactionalAttribute"/>.
    /// Defaults to <see cref="TransactionMode.TouchedOrSingle"/>.
    /// </summary>
    public TransactionMode TransactionMode { get; set; } = TransactionMode.TouchedOrSingle;

    public MediatorOptions RegisterServicesFromAssembly(Assembly assembly)
    { Assemblies.Add(assembly); return this; }
}
