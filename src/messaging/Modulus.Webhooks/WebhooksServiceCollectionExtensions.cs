namespace Modulus.Webhooks;

using System.Net.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Modulus.Authorization.Extensions;
using Modulus.Events;
using Modulus.Events.Abstractions;

/// <summary>The permissions of the webhook management API.</summary>
public static class WebhookPermissions
{
    /// <summary>Manage the current tenant's subscriptions and deliveries.</summary>
    public const string Manage = "webhooks:manage";
}

/// <summary>Chooses the integration events subscriptions can receive.</summary>
public sealed class WebhooksBuilder
{
    internal WebhooksBuilder(IServiceCollection services) => Services = services;

    /// <summary>The service collection.</summary>
    public IServiceCollection Services { get; }

    /// <summary>
    /// Exposes <typeparamref name="TEvent"/> under its stable name (<c>[IntegrationEventName]</c>; name the event, since
    /// subscribers depend on it). The body's <c>data</c> is the event itself, or what <paramref name="payload"/> returns:
    /// map it when the event carries fields that must not leave the system.
    /// </summary>
    public WebhooksBuilder AddEvent<TEvent>(string? description = null, Func<TEvent, object?>? payload = null)
        where TEvent : class, IIntegrationEvent
    {
        var name = IntegrationEventNaming.GetName(typeof(TEvent));
        if (string.Equals(name, WebhookEventCatalog.TestEventName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"'{WebhookEventCatalog.TestEventName}' is reserved for test deliveries.");
        if (name.Contains(',', StringComparison.Ordinal) || name.Contains('*', StringComparison.Ordinal))
            throw new InvalidOperationException($"Webhook event name '{name}' must not contain ',' or '*'.");

        Services.AddSingleton(new WebhookEventDescriptor(name, typeof(TEvent), description)
        {
            Payload = payload is null ? null : e => payload((TEvent)e),
        });
        Services.TryAddEnumerable(ServiceDescriptor.Scoped<IIntegrationEventHandler<TEvent>, WebhookFanOutHandler<TEvent>>());

        // Broker consumers subscribe to the registry's routing keys, so a service that only fans an event out to
        // webhooks still receives it.
        SharedEventRegistry(Services).Register(typeof(TEvent));
        return this;
    }

    private static IntegrationEventRegistry SharedEventRegistry(IServiceCollection services)
    {
        if (services.FirstOrDefault(d => d.ServiceType == typeof(IIntegrationEventRegistry)
                && d.ImplementationInstance is IntegrationEventRegistry)?.ImplementationInstance is IntegrationEventRegistry existing)
            return existing;

        var registry = new IntegrationEventRegistry();
        services.AddSingleton<IIntegrationEventRegistry>(registry);
        return registry;
    }
}

/// <summary>Registers outgoing webhooks.</summary>
public static class WebhooksServiceCollectionExtensions
{
    /// <summary>
    /// Registers webhooks (settings from <c>Webhooks</c>, <see cref="ModulusWebhooksOptions"/>): the events
    /// <paramref name="configure"/> exposes are fanned out to the matching subscriptions of the event's tenant, and a
    /// background worker delivers them, signed per Standard Webhooks. Also declares <see cref="WebhookPermissions.Manage"/>.
    /// The store comes from <see cref="AddModulusWebhooksStore{TContext}"/>; the management API from
    /// <c>MapModulusWebhooks()</c>.
    /// </summary>
    public static IServiceCollection AddModulusWebhooks(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<WebhooksBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        configure?.Invoke(new WebhooksBuilder(services));
        if (services.Any(d => d.ServiceType == typeof(WebhooksMarker)))
            return services;

        services.AddSingleton<WebhooksMarker>();
        services.AddOptions<ModulusWebhooksOptions>()
            .Bind(configuration.GetSection(ModulusWebhooksOptions.SectionName))
            .Validate(o => o.BatchSize > 0 && o.MaxConcurrency > 0, "Webhooks:BatchSize and Webhooks:MaxConcurrency must be positive.")
            .Validate(o => o.RequestTimeout > TimeSpan.Zero && o.LockDuration > o.RequestTimeout,
                "Webhooks:LockDuration must be longer than Webhooks:RequestTimeout.")
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.AddDataProtection();
        services.TryAddSingleton<WebhookSecretProtector>();
        services.TryAddSingleton<IWebhookEventCatalog, WebhookEventCatalog>();
        services.TryAddScoped<WebhookDeliveryProcessor>();
        services.AddHostedService<WebhookDeliveryService>();

        services.AddHttpClient(WebhookDeliveryProcessor.HttpClientName)
            .ConfigureHttpClient((sp, client) =>
                client.Timeout = sp.GetRequiredService<IOptions<ModulusWebhooksOptions>>().Value.RequestTimeout)
            .ConfigurePrimaryHttpMessageHandler(sp =>
            {
                var settings = sp.GetRequiredService<IOptions<ModulusWebhooksOptions>>().Value;
                var handler = new SocketsHttpHandler
                {
                    // A redirect would leave the checked URL (and could lead into the private network).
                    AllowAutoRedirect = false,
                    UseCookies = false,
                    ConnectTimeout = TimeSpan.FromSeconds(10),
                    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                };
                if (!settings.AllowPrivateNetworks)
                {
                    // Connect only to checked addresses; a proxy would connect on our behalf, unchecked.
                    handler.UseProxy = false;
                    handler.ConnectCallback = WebhookAddressGuard.ConnectAsync;
                }

                return handler;
            });

        // A host with AddModulusAuthorization resolves ':' policies through its grant store; this named policy is the
        // fallback for one without it (a "permission" claim), so the endpoints never fail with "policy not found".
        services.AddAuthorization(o => o.AddPolicy(WebhookPermissions.Manage, p => p
            .RequireAuthenticatedUser()
            .RequireClaim("permission", WebhookPermissions.Manage)));
        services.AddPermissions("Modulus.Webhooks", registry => registry.Add(
            WebhookPermissions.Manage,
            "Manage webhook subscriptions and deliveries: create, change, test and retry."));
        return services;
    }

    /// <summary>
    /// Stores subscriptions and deliveries in <typeparamref name="TContext"/> (registered by the caller, e.g. with
    /// <c>AddDbContext</c>). It is also registered as a <see cref="DbContext"/>, so <c>MigrateModulusDatabasesAsync</c>
    /// migrates it with the modules.
    /// </summary>
    public static IServiceCollection AddModulusWebhooksStore<TContext>(this IServiceCollection services)
        where TContext : ModulusWebhooksDbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(d => d.ServiceType == typeof(ModulusWebhooksDbContext)))
            return services;
        services.AddScoped<ModulusWebhooksDbContext>(sp => sp.GetRequiredService<TContext>());
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<TContext>());
        return services;
    }

    private sealed class WebhooksMarker;
}
