using Microsoft.Extensions.DependencyInjection;
using Modulus.Messaging.Internal;

namespace Modulus.Messaging;

/// <summary>Collects consumers, bus-wide defaults and the transport before the bus is built.</summary>
public sealed class MessagingBusBuilder
{
    private readonly List<(ConsumerRegistration Registration, ConsumerOptions Own)> _consumers = [];
    private readonly ConsumerOptions _defaults = new();
    private Func<IServiceProvider, IMessageTransport>? _transportFactory;
    private string _replyEndpoint = "replies-" + Guid.NewGuid().ToString("N");
    private readonly HashSet<Type> _replyTypes = [];

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
                return consumer.Consume(new ConsumeContext<TMessage>(envelope, cancellationToken) { Services = services });
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

    /// <summary>
    /// Names the reply endpoint of this process (FR-06). The default is unique per process; a fixed name lets one instance
    /// keep its reply queue across restarts. Call it before <see cref="AddRequestClient{TResponse}"/>.
    /// </summary>
    public MessagingBusBuilder UseReplyEndpoint(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("The reply endpoint name must not be empty.", nameof(name));
        }

        if (_replyTypes.Count > 0)
        {
            throw new InvalidOperationException("Call UseReplyEndpoint before AddRequestClient.");
        }

        _replyEndpoint = name;
        return this;
    }

    /// <summary>
    /// Lets this process receive responses of type <typeparamref name="TResponse"/> for <see cref="IRequestClient"/>.
    /// Each response type is received on the reply endpoint once.
    /// </summary>
    public MessagingBusBuilder AddRequestClient<TResponse>()
        where TResponse : class
    {
        if (!_replyTypes.Add(typeof(TResponse)))
        {
            return this;
        }

        return AddConsumer<ReplyReceiver<TResponse>, RequestReply<TResponse>>(_replyEndpoint);
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
        return new MessagingOptions(consumers, _transportFactory, _replyEndpoint);
    }
}
