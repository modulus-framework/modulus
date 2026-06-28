using System.Reflection;
using Rebus.Config;

namespace Modulus.Sagas.Extensions;

/// <summary>
/// Fluent builder used by <c>SagaServiceCollectionExtensions.AddModulusSagas</c>
/// to configure the Rebus bus, Polly retry pipeline, and handler registration.
/// </summary>
public sealed class SagaConfigurationBuilder
{
    private Func<RebusConfigurer, IServiceProvider, RebusConfigurer>? _rebusConfigurer;
    private PollyRetryOptions? _pollyOptions;
    private readonly List<Assembly> _handlerAssemblies = [];
    private bool _replaceModuleBus;
    private bool _replaceOutboxDispatcher;

    /// <summary>
    /// Sets the Rebus configuration delegate (transport, persistence, options).
    /// The delegate receives the Rebus <c>RebusConfigurer</c> and the
    /// <see cref="IServiceProvider"/> so it can resolve services if needed.
    /// </summary>
    public SagaConfigurationBuilder Rebus(
        Func<RebusConfigurer, IServiceProvider, RebusConfigurer> configure)
    {
        _rebusConfigurer = configure;
        return this;
    }

    /// <summary>
    /// Sets the Rebus configuration delegate (transport, persistence, options).
    /// Simpler overload without service-provider access.
    /// </summary>
    public SagaConfigurationBuilder Rebus(
        Func<RebusConfigurer, RebusConfigurer> configure)
    {
        _rebusConfigurer = (cfg, _) => configure(cfg);
        return this;
    }

    /// <summary>
    /// Enables a Polly retry pipeline in the Rebus incoming-message pipeline.
    /// Transient handler failures are retried with configurable back-off
    /// and (optionally) circuit-breaker protection.
    /// </summary>
    public SagaConfigurationBuilder PollyRetry(Action<PollyRetryOptions> configure)
    {
        _pollyOptions = new PollyRetryOptions();
        configure(_pollyOptions);
        return this;
    }

    /// <summary>
    /// Registers all Rebus handlers (sagas and <c>IHandleMessages&lt;T&gt;</c>)
    /// found in the assembly containing <typeparamref name="T"/>.
    /// </summary>
    public SagaConfigurationBuilder HandlersFromAssemblyOf<T>()
    {
        _handlerAssemblies.Add(typeof(T).Assembly);
        return this;
    }

    /// <summary>
    /// Registers all Rebus handlers from the given assemblies.
    /// </summary>
    public SagaConfigurationBuilder HandlersFromAssemblies(params Assembly[] assemblies)
    {
        _handlerAssemblies.AddRange(assemblies);
        return this;
    }

    /// <summary>
    /// Replaces the default <see cref="global::Modulus.Events.Abstractions.IModuleBus"/>
    /// with a Rebus-backed implementation so that
    /// <c>IModuleBus.PublishAsync</c> routes through the Rebus transport.
    /// </summary>
    public SagaConfigurationBuilder ReplaceModuleBus()
    {
        _replaceModuleBus = true;
        return this;
    }

    /// <summary>
    /// Replaces the default outbox dispatcher with a Rebus-backed one so
    /// outbox messages are published through the Rebus transport.
    /// </summary>
    public SagaConfigurationBuilder ReplaceOutboxDispatcher()
    {
        _replaceOutboxDispatcher = true;
        return this;
    }

    // ── Internal accessors ─────────────────────────────────────────

    internal Func<RebusConfigurer, IServiceProvider, RebusConfigurer> RebusConfigurer =>
        _rebusConfigurer ?? ((cfg, _) => cfg);

    internal PollyRetryOptions? PollyOptions => _pollyOptions;

    internal IReadOnlyList<Assembly> HandlerAssemblies => _handlerAssemblies;

    internal bool ShouldReplaceModuleBus => _replaceModuleBus;

    internal bool ShouldReplaceOutboxDispatcher => _replaceOutboxDispatcher;
}
