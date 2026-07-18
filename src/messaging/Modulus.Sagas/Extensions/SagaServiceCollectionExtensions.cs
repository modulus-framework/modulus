using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using global::Polly;
using Rebus.Bus;
using Rebus.Config;
using Rebus.Pipeline;
using Rebus.Pipeline.Receive;
using Rebus.ServiceProvider;
using Modulus.Events.Abstractions;
using Modulus.Outbox.Abstractions;
using Modulus.Sagas.Bus;
using Modulus.Sagas.Resilience;

namespace Modulus.Sagas.Extensions;

public static class SagaServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Rebus saga infrastructure: transport, saga persistence,
    /// optional Polly retry pipeline, and handler auto-registration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Fluent configuration callback.</param>
    public static IServiceCollection AddModulusSagas(
        this IServiceCollection services,
        Action<SagaConfigurationBuilder> configure)
    {
        var builder = new SagaConfigurationBuilder();
        configure(builder);

        var userConfigurer = builder.RebusConfigurer;
        var pollyOptions = builder.PollyOptions;

        // ── Compose the Rebus configuration ───────────────────────
        Func<RebusConfigurer, IServiceProvider, RebusConfigurer> rebusSetup = (cfg, sp) =>
        {
            cfg.Logging(l => l.MicrosoftExtensionsLogging(
                sp.GetService<ILoggerFactory>()));

            if (pollyOptions is not null)
            {
                var pipeline = PollyPipelineFactory.Create(pollyOptions);
                cfg.Options(o => o.Decorate<IPipeline>(c =>
                {
                    var inner = c.Get<IPipeline>();
                    var step = new PollyRetryStep(
                        pipeline,
                        c.Get<ILogger<PollyRetryStep>>());
                    return new PipelineStepInjector(inner)
                        .OnReceive(step, PipelineRelativePosition.Before,
                            typeof(ActivateHandlersStep));
                }));
            }

            return userConfigurer(cfg, sp);
        };

        services.AddRebus(rebusSetup);

        // ── Register handlers ──────────────────────────────────────
        foreach (var assembly in builder.HandlerAssemblies.Distinct())
            services.AutoRegisterHandlersFromAssembly(assembly);

        // ── Bridge IIntegrationEventHandler<T> → IHandleMessages<T> ──
        RegisterIntegrationEventHandlerAdapters(services, builder.HandlerAssemblies);

        // ── Replace IModuleBus ──────────────────────────────────────
        if (builder.ShouldReplaceModuleBus)
        {
            services.RemoveAll<IModuleBus>();
            services.AddScoped<IModuleBus>(sp =>
                new RebusModuleBus(
                    sp.GetRequiredService<IBus>(),
                    sp.GetService<ILogger<RebusModuleBus>>()));
        }

        // ── Replace IOutboxDispatcher ───────────────────────────────
        if (builder.ShouldReplaceOutboxDispatcher)
        {
            services.RemoveAll<IOutboxDispatcher>();
            services.AddScoped<IOutboxDispatcher>(sp =>
                new RebusOutboxDispatcher(
                    sp.GetRequiredService<IBus>(),
                    sp.GetRequiredService<IIntegrationEventRegistry>(),
                    sp.GetService<ILogger<RebusOutboxDispatcher>>()));
        }

        return services;
    }

    /// <summary>
    /// Convenience overload that accepts the Rebus configuration and handler
    /// assemblies directly, with sensible Polly defaults.
    /// </summary>
    public static IServiceCollection AddModulusSagas(
        this IServiceCollection services,
        Func<RebusConfigurer, RebusConfigurer> configureRebus,
        Action<PollyRetryOptions>? configurePolly = null,
        params Assembly[] handlerAssemblies)
    =>
        services.AddModulusSagas(builder =>
        {
            builder.Rebus(configureRebus);

            if (configurePolly is not null)
                builder.PollyRetry(configurePolly);

            if (handlerAssemblies.Length > 0)
                builder.HandlersFromAssemblies(handlerAssemblies);

            builder.ReplaceModuleBus().ReplaceOutboxDispatcher();
        });

    /// <summary>
    /// Scans <paramref name="assemblies"/> for concrete
    /// <see cref="IIntegrationEventHandler{TEvent}"/> implementations and
    /// registers a <see cref="IntegrationEventHandlerAdapter{TEvent}"/> for
    /// each unique event type.  The adapter bridges Modulus integration-event
    /// handlers to Rebus's <c>IHandleMessages&lt;T&gt;</c> pipeline.
    /// </summary>
    private static void RegisterIntegrationEventHandlerAdapters(
        IServiceCollection services, IReadOnlyList<Assembly> assemblies)
    {
        if (assemblies.Count == 0) return;

        var handlerInterface = typeof(IIntegrationEventHandler<>);
        var rebusHandleType = typeof(Rebus.Handlers.IHandleMessages<>);
        var adapterType = typeof(IntegrationEventHandlerAdapter<>);

        var eventTypes = new HashSet<Type>();

        foreach (var assembly in assemblies)
        {
            foreach (var type in assembly.GetTypes()
                         .Where(t => t is { IsAbstract: false, IsInterface: false }))
            {
                foreach (var iface in type.GetInterfaces())
                {
                    if (iface.IsGenericType
                        && iface.GetGenericTypeDefinition() == handlerInterface)
                    {
                        eventTypes.Add(iface.GetGenericArguments()[0]);
                    }
                }
            }
        }

        foreach (var eventType in eventTypes)
        {
            var serviceType = rebusHandleType.MakeGenericType(eventType);
            var adapterImpl = adapterType.MakeGenericType(eventType);
            services.TryAddScoped(serviceType, adapterImpl);
        }
    }
}
