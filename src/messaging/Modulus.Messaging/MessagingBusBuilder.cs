using Microsoft.Extensions.DependencyInjection;
using Modulus.Messaging.Internal;

namespace Modulus.Messaging;

/// <summary>Collects consumers, bus-wide defaults and the transport before the bus is built.</summary>
public sealed class MessagingBusBuilder
{
    private readonly List<(ConsumerRegistration Registration, ConsumerOptions Own)> _consumers = [];
    private readonly ConsumerOptions _defaults = new();
    private Func<IServiceProvider, IMessageTransport>? _transportFactory;

    /// <summary>
    /// Registers <typeparamref name="TConsumer"/> for <typeparamref name="TMessage"/> on an endpoint.
    /// The endpoint defaults to the consumer's name in kebab-case without the <c>Consumer</c> suffix.
    /// <paramref name="configure"/> sets retry, redelivery or timeout for this consumer only.
    /// </summary>
    public MessagingBusBuilder AddConsumer<TConsumer, TMessage>(string? endpointName = null, Action<ConsumerOptions>? configure = null)
        where TConsumer : class, IConsumer<TMessage>
        where TMessage : class
    {
        var endpoint = endpointName ?? EndpointNames.ForConsumer(typeof(TConsumer));
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new ArgumentException("Endpoint name must not be empty.", nameof(endpointName));
        }

        var own = new ConsumerOptions();
        configure?.Invoke(own);

        var registration = new ConsumerRegistration(
            typeof(TConsumer).FullName!,
            endpoint,
            typeof(TMessage),
            static (services, envelope, cancellationToken) =>
            {
                // A consumer is resolved from the scope of the attempt being run (RULE-07).
                var consumer = ActivatorUtilities.GetServiceOrCreateInstance<TConsumer>(services);
                return consumer.Consume(new ConsumeContext<TMessage>(envelope, cancellationToken));
            },
            new ConsumerOptions(),
            static (bus, envelope, exception, cancellationToken) => bus.Publish(
                new Fault<TMessage>(
                    envelope.MessageId,
                    envelope.Endpoint,
                    (TMessage)envelope.Message,
                    exception.GetType().FullName ?? exception.GetType().Name,
                    exception.Message,
                    DateTimeOffset.UtcNow),
                cancellationToken));

        _consumers.Add((registration, own));
        return this;
    }

    /// <summary>Sets retry, redelivery or timeout for every consumer that does not set its own.</summary>
    public MessagingBusBuilder ConfigureDefaults(Action<ConsumerOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(_defaults);
        return this;
    }

    /// <summary>Chooses the transport the bus sends and receives over.</summary>
    public MessagingBusBuilder UseTransport(Func<IServiceProvider, IMessageTransport> factory)
    {
        _transportFactory = factory ?? throw new ArgumentNullException(nameof(factory));
        return this;
    }

    internal MessagingOptions Build()
    {
        if (_transportFactory is null)
        {
            throw new InvalidOperationException("No transport is configured. Call UseTransport(...) or a transport extension such as UseInMemory().");
        }

        var consumers = _consumers
            .Select(c =>
            {
                var effective = c.Own.Inherit(_defaults);
                effective.Validate();
                return c.Registration with { Options = effective };
            })
            .ToArray();
        return new MessagingOptions(consumers, _transportFactory);
    }
}
