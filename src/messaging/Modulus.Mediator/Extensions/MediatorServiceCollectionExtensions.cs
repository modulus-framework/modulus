using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Modulus.Mediator.Extensions;

using System.Reflection;
using Modulus.Core.Abstractions;
using Modulus.Core.Null;
using Modulus.Mediator.Abstractions;
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

        // Ensure the pipeline behaviors' dependencies are always available.
        // TryAdd never overwrites a registration the host already made, so a
        // real Identity module (ICurrentUser) or caching configuration
        // (IMemoryCache) takes precedence over these fail-safe defaults.
        services.TryAddSingleton<IMemoryCache, MemoryCache>();
        services.TryAddScoped<ICurrentUser, NullCurrentUser>();

        // Register handlers from specified assemblies
        foreach (var assembly in opts.Assemblies)
            RegisterHandlers(services, assembly);

        // Register behaviors in order (first registered = outermost)
        if (opts.EnableLogging)
            services.AddScoped(typeof(IPipelineBehavior<,>),
                typeof(LoggingBehavior<,>));

        if (opts.EnableValidation)
            services.AddScoped(typeof(IPipelineBehavior<,>),
                typeof(ValidationBehavior<,>));

        if (opts.EnableAuthorization)
            services.AddScoped(typeof(IPipelineBehavior<,>),
                typeof(AuthorizationBehavior<,>));

        if (opts.EnableCaching)
            services.AddScoped(typeof(IPipelineBehavior<,>),
                typeof(CachingBehavior<,>));

        if (opts.EnableTransaction)
            services.AddScoped(typeof(IPipelineBehavior<,>),
                typeof(TransactionBehavior<,>));

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

    public MediatorOptions RegisterServicesFromAssembly(Assembly assembly)
    { Assemblies.Add(assembly); return this; }
}