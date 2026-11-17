using Microsoft.Extensions.DependencyInjection;
using Modulus.Messaging.Internal;

namespace Modulus.Messaging;

/// <summary>Collects consumers, bus-wide defaults and the transport before the bus is built.</summary>
public sealed class MessagingBusBuilder
{
    /// <summary>The body size limit when none is set (FR-22): 1 MiB.</summary>
    public const int DefaultMaxMessageBytes = 1024 * 1024;

    private readonly List<(ConsumerRegistration Registration, ConsumerOptions Own)> _consumers = [];
    private readonly ConsumerOptions _defaults = new();

    /// <summary>The message types the bus may deserialize from the wire (NFR-10), read from the registered consumers.</summary>
    internal MessageTypeAllowList MessageTypes =>
        // A sealed message (FR-22) travels as its EncryptedPayload wrapper, so the wrapper is always an allowed type.
        _messageTypes ??= new MessageTypeAllowList(() => _consumers.Select(c => c.Registration.MessageType).Append(typeof(EncryptedPayload)));

    private MessageTypeAllowList? _messageTypes;
    private readonly List<Type> _filters = [];
    private readonly List<Type> _observers = [];
    private Func<IServiceProvider, IMessageTransport>? _transportFactory;
    private Func<string, string>? _endpointNameFormatter;
    private string _replyEndpoint = "replies-" + Guid.NewGuid().ToString("N");
    private readonly HashSet<Type> _replyTypes = [];
    private bool _replyFaultReceiverAdded;
    private int _maxMessageBytes = DefaultMaxMessageBytes;
    private bool _deployOnly;
    private Func<Type, string> _wireNames = MessageUrn.Of;

    /// <summary>
    /// Registers <typeparamref name="TConsumer"/> with the endpoint and settings of <paramref name="definition"/> (FR-03).
    /// </summary>
    public MessagingBusBuilder AddConsumer<TConsumer, TMessage>(ConsumerDefinition<TConsumer> definition)
        where TConsumer : class, IConsumer<TMessage>
        where TMessage : class
    {
        ArgumentNullException.ThrowIfNull(definition);
        return AddConsumer<TConsumer, TMessage>(definition.EndpointName, definition.Configure);
    }

    /// <summary>
    /// Registers <typeparamref name="TConsumer"/> for <typeparamref name="TMessage"/> on an endpoint.
    /// The endpoint defaults to the consumer's name in kebab-case without the <c>Consumer</c> suffix, passed through the
    /// name formatter when one is set (see <see cref="UseEndpointNameFormatter"/>).
    /// <paramref name="configure"/> sets retry, redelivery or timeout for this consumer only.
    /// </summary>
    public MessagingBusBuilder AddConsumer<TConsumer, TMessage>(string? endpointName = null, Action<ConsumerOptions>? configure = null)
        where TConsumer : class, IConsumer<TMessage>
        where TMessage : class
    {
        var endpoint = ResolveEndpoint(endpointName, typeof(TConsumer));
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
            PublishFaultOf<TMessage>());

        _consumers.Add((registration, own));
        return this;
    }

    /// <summary>
    /// Registers a batch consumer (FR-04): <typeparamref name="TConsumer"/> receives up to <see cref="ConsumerOptions.BatchSize"/>
    /// messages of <typeparamref name="TMessage"/> at once, and a batch runs when it is full or when
    /// <see cref="ConsumerOptions.BatchWindow"/> has passed since its first message. A failed batch is retried, redelivered or
    /// parked message by message, with the consumer's settings.
    /// </summary>
    public MessagingBusBuilder AddBatchConsumer<TConsumer, TMessage>(string? endpointName = null, Action<ConsumerOptions>? configure = null)
        where TConsumer : class, IConsumer<Batch<TMessage>>
        where TMessage : class
    {
        var endpoint = ResolveEndpoint(endpointName, typeof(TConsumer));
        var own = new ConsumerOptions();
        configure?.Invoke(own);

        var registration = new ConsumerRegistration(
            typeof(TConsumer).FullName!,
            endpoint,
            typeof(TMessage),
            static (_, _, _) => throw new InvalidOperationException("A batch consumer is invoked with its batch, never with one message."),
            new ConsumerOptions(),
            PublishFaultOf<TMessage>(),
            static (services, envelopes, cancellationToken) =>
            {
                var consumer = ActivatorUtilities.GetServiceOrCreateInstance<TConsumer>(services);
                var contexts = envelopes
                    .Select(e => new ConsumeContext<TMessage>(e, cancellationToken) { Services = services })
                    .ToList();
                var first = envelopes[0];
                var batchEnvelope = new MessageEnvelope(first.MessageId, first.Endpoint, new Batch<TMessage>(contexts), first.Headers, first.ConsumerKey);
                return consumer.Consume(new ConsumeContext<Batch<TMessage>>(batchEnvelope, cancellationToken) { Services = services });
            });

        _consumers.Add((registration, own));
        return this;
    }

    private static Func<IBus, MessageEnvelope, Exception, CancellationToken, Task> PublishFaultOf<TMessage>() where TMessage : class =>
        static (bus, envelope, exception, cancellationToken) => bus.Publish(
            new Fault<TMessage>(
                envelope.MessageId,
                envelope.Endpoint,
                (TMessage)envelope.Message,
                exception.GetType().FullName ?? exception.GetType().Name,
                exception.Message,
                DateTimeOffset.UtcNow),
            cancellationToken);

    private string ResolveEndpoint(string? endpointName, Type consumer)
    {
        var endpoint = endpointName ?? (_endpointNameFormatter is null
            ? EndpointNames.ForConsumer(consumer)
            : _endpointNameFormatter(EndpointNames.ForConsumer(consumer)));
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new ArgumentException("Endpoint name must not be empty.", nameof(endpointName));
        }

        return endpoint;
    }

    /// <summary>
    /// Changes the endpoint names the bus derives for consumers that have no explicit name (FR-23). Call it before the
    /// consumers are added; explicit names are used as given.
    /// </summary>
    public MessagingBusBuilder UseEndpointNameFormatter(Func<string, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        _endpointNameFormatter = formatter;
        return this;
    }

    /// <summary>
    /// Deploy-only mode (FR-23): the host declares the topology (queues, schema) on start and then exits, without
    /// receiving any message. Use it in a deployment job that holds the rights to create the topology (NFR-11).
    /// </summary>
    /// <summary>
    /// Sends the .NET assembly-qualified name of each message type instead of its URN, for one release of a rolling upgrade.
    /// Use it while services that still expect the .NET name run beside services that send URNs; receivers accept both,
    /// so only the senders need it. The setting is process-wide, and it is a deployment flag: remove it once every consumer
    /// is on a build that reads the URN.
    /// </summary>
    public MessagingBusBuilder UseLegacyWireTypeNames() =>
        UseMessageNameFormatter(type => type.AssemblyQualifiedName ?? type.FullName ?? type.Name);

    /// <summary>
    /// Chooses the type name each message is sent under (MassTransit's message name formatter). The default is the message's
    /// URN: its <c>[MessageUrn]</c>, or a name derived from its full name. The choice belongs to this bus, so two buses in one
    /// process can differ. Receivers accept the URN and the assembly-qualified name either way.
    /// </summary>
    public MessagingBusBuilder UseMessageNameFormatter(Func<Type, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        _wireNames = formatter;
        return this;
    }

    public MessagingBusBuilder UseDeployOnly()
    {
        _deployOnly = true;
        return this;
    }

    /// <summary>
    /// The largest message body the bus sends (FR-22), in bytes of its JSON form. A larger message is refused at send with
    /// <see cref="MessageTooLargeException"/>. The default is <see cref="DefaultMaxMessageBytes"/>.
    /// </summary>
    public MessagingBusBuilder UseMaxMessageSize(int bytes)
    {
        if (bytes < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(bytes), "Must be at least 1.");
        }

        _maxMessageBytes = bytes;
        return this;
    }

    /// <summary>
    /// Adds a filter that runs around every send, publish and consume (FR-20). Resolved from the container per message, so
    /// register it as a singleton or scoped service when it keeps state. Filters run in the order they are added.
    /// </summary>
    public MessagingBusBuilder AddFilter<TFilter>()
        where TFilter : class, IMessageFilter
    {
        _filters.Add(typeof(TFilter));
        return this;
    }

    /// <summary>Adds an observer of the bus's activity (FR-20). Observers are created once, when the bus is built.</summary>
    public MessagingBusBuilder AddObserver<TObserver>()
        where TObserver : class, IMessageObserver
    {
        _observers.Add(typeof(TObserver));
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
    /// Each response type is received on the reply endpoint once. The reply endpoint also receives remote faults (FR-06).
    /// </summary>
    public MessagingBusBuilder AddRequestClient<TResponse>()
        where TResponse : class
    {
        if (!_replyFaultReceiverAdded)
        {
            _replyFaultReceiverAdded = true;
            AddConsumer<RequestFaultReceiver, RequestFault>(_replyEndpoint);
        }

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

        MessageUrn.EnsureUnique(_consumers.Select(c => c.Registration.MessageType));

        var consumers = _consumers
            .Select(c =>
            {
                var effective = c.Own.Inherit(_defaults);
                effective.Validate();
                return c.Registration with { Options = effective };
            })
            .ToArray();
        return new MessagingOptions(consumers, _transportFactory, _replyEndpoint, _maxMessageBytes, _filters.ToArray(), _observers.ToArray(), _deployOnly, MessageTypes, _wireNames);
    }
}
