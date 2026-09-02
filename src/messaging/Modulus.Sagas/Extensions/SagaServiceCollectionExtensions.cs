using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using global::Polly;
using Rebus.Bus;
using Rebus.Config;
using Rebus.Exceptions;
using Rebus.Injection;
using Rebus.Pipeline;
using Rebus.Pipeline.Receive;
using Rebus.ServiceProvider;
using Modulus.Core.Abstractions;
using Modulus.Core.Null;
using Modulus.Events.Abstractions;
using Modulus.Outbox.Abstractions;
using Modulus.Sagas.Bus;
using Modulus.Sagas.Pipeline;
using Modulus.Sagas.Resilience;

namespace Modulus.Sagas.Extensions;

public static class SagaServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Rebus saga infrastructure: transport, saga persistence,
    /// optional Polly retry pipeline, ambient tenant/correlation propagation,
    /// and handler auto-registration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Fluent configuration callback.</param>
    /// <remarks>
    /// Sagas are authored with Rebus's native <c>Saga&lt;TData&gt;</c> model —
    /// this package wires them into Modulus rather than replacing that model:
    /// <code>
    /// public sealed class OrderProcessSaga : Saga&lt;OrderProcessData&gt;,
    ///     IAmInitiatedBy&lt;OrderSubmitted&gt;, IHandleMessages&lt;OrderPaid&gt;
    /// {
    ///     protected override void CorrelateMessages(ICorrelationConfig&lt;OrderProcessData&gt; cfg)
    ///     {
    ///         cfg.Correlate&lt;OrderSubmitted&gt;(m =&gt; m.OrderId, d =&gt; d.OrderId, d =&gt; d.OrderId = m.OrderId);
    ///         cfg.Correlate&lt;OrderPaid&gt;(m =&gt; m.OrderId, d =&gt; d.OrderId);
    ///     }
    ///     // Handle(...) methods transition the saga state machine...
    /// }
    ///
    /// // Program.cs
    /// services.AddModulusSagas(b => b
    ///     .Rebus(cfg => cfg.Transport(t => t.UseRabbitMq(...)).Sagas(s => s.StoreInSql(...)))
    ///     .PollyRetry(o => o.MaxRetryAttempts = 5)
    ///     .HandlersFromAssemblyOf&lt;OrderProcessSaga&gt;()
    ///     .ReplaceModuleBus()          // publish IModuleBus events via Rebus
    ///     .ReplaceOutboxDispatcher()); // relay outbox rows via Rebus
    /// </code>
    /// Remember to enable saga persistence in your Rebus config
    /// (<c>cfg.Options(o => o.EnableSagas())</c> plus a saga store).
    /// </remarks>
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

            cfg.Options(o =>
            {
                // Restore the publisher's tenant/correlation context on every
                // incoming message so handlers run in the right business
                // context (tenant query filters, log correlation).
                o.Decorate<IPipeline>(c =>
                {
                    var inner = c.Get<IPipeline>();
                    var step = new AmbientContextIncomingStep(
                        ResolveOrNull<ICurrentTenant>(c),
                        ResolveOrNull<ICorrelationContext>(c));
                    return new PipelineStepInjector(inner)
                        .OnReceive(step, PipelineRelativePosition.Before,
                            typeof(ActivateHandlersStep));
                });

                if (pollyOptions is not null)
                {
                    var pipeline = PollyPipelineFactory.Create(pollyOptions);
                    o.Decorate<IPipeline>(c =>
                    {
                        var inner = c.Get<IPipeline>();
                        var step = new PollyRetryStep(
                            pipeline,
                            c.Get<ILogger<PollyRetryStep>>());
                        return new PipelineStepInjector(inner)
                            .OnReceive(step, PipelineRelativePosition.Before,
                                typeof(ActivateHandlersStep));
                    });
                }
            });

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
                    sp.GetService<ICurrentTenant>() ?? new NullCurrentTenant(),
                    sp.GetService<ICorrelationContext>(),
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
                    sp.GetRequiredService<IMessageSerializer>(),
                    sp.GetService<ILogger<RebusOutboxDispatcher>>()));
        }

        return services;
    }

    /// <summary>
    /// Resolves a service from Rebus's resolution context when registered;
    /// returns <see langword="null"/> instead of throwing for optional
    /// dependencies (e.g. a messaging-only host without tenancy/correlation).
    /// </summary>
    private static T? ResolveOrNull<T>(IResolutionContext context)
        where T : class
    {
        try
        {
            return context.Get<T>();
        }
        catch (ResolutionException)
        {
            return null;
        }
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
