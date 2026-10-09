namespace Modulus.Grpc;

/// <summary>Server settings, bound from the <c>Grpc</c> configuration section.</summary>
public sealed class ModulusGrpcOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Grpc";

    /// <summary>
    /// Puts the exception text in the status of an unexpected server error. Development only: it reveals internals.
    /// Client errors (validation, not found, ...) carry their details either way.
    /// </summary>
    public bool EnableDetailedErrors { get; set; }

    /// <summary>
    /// Maps the gRPC reflection service (what <c>grpcurl</c> and Postman list services with). It describes every
    /// service and message, so leave it off in Production.
    /// </summary>
    public bool EnableReflection { get; set; }

    /// <summary>
    /// Lets browsers call the services with grpc-web (<c>UseModulusGrpcWeb</c> translates the requests). Off by default.
    /// The CORS policy must allow the <c>x-grpc-web</c>, <c>x-user-agent</c> and <c>grpc-timeout</c> request headers and expose
    /// <c>grpc-status</c>, <c>grpc-message</c> and <c>grpc-status-details-bin</c>.
    /// </summary>
    public bool EnableGrpcWeb { get; set; }

    /// <summary>Maps <c>grpc.health.v1.Health</c>, backed by the registered health checks (module checks included).</summary>
    public bool EnableHealthChecks { get; set; } = true;

    /// <summary>The largest message the server accepts, in bytes (gRPC's default is 4 MB). Null keeps the default.</summary>
    public int? MaxReceiveMessageSize { get; set; }

    /// <summary>The largest message the server sends, in bytes. Null means no limit (gRPC's default).</summary>
    public int? MaxSendMessageSize { get; set; }

    /// <summary>
    /// Compresses responses with this algorithm (<c>gzip</c> is built in) for clients that accept it. Null leaves
    /// responses uncompressed, gRPC's default. Worth it for large list messages over a slow link; skip it between
    /// services on one network.
    /// </summary>
    public string? ResponseCompressionAlgorithm { get; set; }
}

/// <summary>
/// Defaults for clients registered with <c>AddModulusGrpcClient</c>, bound from <c>Grpc:Client</c>.
/// </summary>
public sealed class ModulusGrpcClientOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Grpc:Client";

    /// <summary>
    /// Attempts per call, the first included, retried only on <c>Unavailable</c> (the server was not reached or is
    /// shutting down), so a call is never repeated after the server acted on it. 1 turns retries off.
    /// </summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>The first retry's back-off ceiling (the actual delay is random below it).</summary>
    public TimeSpan InitialBackoff { get; set; } = TimeSpan.FromMilliseconds(200);

    /// <summary>The largest back-off ceiling.</summary>
    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// The deadline given to a unary call that has none (no deadline means a call can wait forever on a stuck server).
    /// Streaming calls are left alone. Null turns it off. Inside a gRPC service the incoming call's deadline is
    /// propagated first, so a shorter one wins.
    /// </summary>
    public TimeSpan? DefaultDeadline { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How often an HTTP/2 PING is sent while a call is active, so a dead connection (a load balancer that silently
    /// dropped it) is noticed instead of the call hanging until its deadline. Null turns pings off.
    /// </summary>
    public TimeSpan? KeepAlivePingDelay { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>How long to wait for the answer to a keep-alive PING before the connection is closed.</summary>
    public TimeSpan KeepAlivePingTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
