namespace Modulus.GraphQL;

/// <summary>Server settings, bound from the <c>GraphQL</c> configuration section.</summary>
public sealed class ModulusGraphQLOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "GraphQL";

    /// <summary>The endpoint <c>MapModulusGraphQL()</c> maps (relative to the route group it is mapped on).</summary>
    public string Path { get; set; } = "/graphql";

    /// <summary>
    /// Answers <c>401</c> to an unauthenticated request before it is parsed. Fields still check their own policies
    /// (<c>AuthorizeWithPolicy</c>), so turning this off only opens the fields that declare none.
    /// </summary>
    public bool RequireAuthenticatedUser { get; set; } = true;

    /// <summary>
    /// Allows introspection (<c>__schema</c>, <c>__type</c>), which is what IDEs and code generators read the schema
    /// with. It describes every type and field, so leave it off in Production.
    /// </summary>
    public bool EnableIntrospection { get; set; }

    /// <summary>Maps the GraphiQL IDE at <c>{Path}/ui</c> (loaded from a CDN). Development only.</summary>
    public bool EnableUi { get; set; }

    /// <summary>The deepest selection a request may nest (introspection's own depth counts too). Null means no limit.</summary>
    public int? MaxDepth { get; set; } = 15;

    /// <summary>
    /// The highest complexity a request may have: each field costs 1 and a list multiplies its children by
    /// <see cref="ListSizeEstimate"/>. Null means no limit.
    /// </summary>
    public int? MaxComplexity { get; set; } = 1000;

    /// <summary>
    /// The longest query document accepted, in characters; a longer one is rejected before it is validated further.
    /// Complexity and depth do not bound how large the text itself is. Null means no limit.
    /// </summary>
    public int? MaxDocumentLength { get; set; } = 20_000;

    /// <summary>
    /// The most aliased fields one request may use (<c>a: product(id: 1) b: product(id: 2) ...</c>). Aliases let a single
    /// request run the same expensive field many times, which depth and complexity limits can miss. Null means no limit.
    /// </summary>
    public int? MaxAliases { get; set; } = 20;

    /// <summary>
    /// How long one request may execute before it is cancelled (its resolvers see the cancellation token). Null means no
    /// limit beyond the server's own request timeout.
    /// </summary>
    public TimeSpan? ExecutionTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Persisted query settings (<c>GraphQL:PersistedQueries</c>).</summary>
    public PersistedQueryOptions PersistedQueries { get; set; } = new();

    /// <summary>The average list length the complexity estimate assumes.</summary>
    public double ListSizeEstimate { get; set; } = 5;

    /// <summary>Accepts a JSON array of requests in one POST. Off by default: a batch multiplies a request's cost.</summary>
    public bool EnableBatchedRequests { get; set; }

    /// <summary>
    /// Resolves a query's fields concurrently. Off by default: resolvers share the request's scoped services (one
    /// <c>DbContext</c> per module), which do not support concurrent use. DataLoaders batch either way.
    /// </summary>
    public bool ParallelQueryExecution { get; set; }

    /// <summary>
    /// Puts the exception text (type, message, stack) in an unexpected error. Development only: it reveals internals.
    /// Client errors (validation, not found, ...) carry their details either way.
    /// </summary>
    public bool ExposeExceptionDetails { get; set; }
}

/// <summary>Settings for <see cref="ModulusGraphQLOptions.PersistedQueries"/>.</summary>
public sealed class PersistedQueryOptions
{
    /// <summary>
    /// <see cref="PersistedQueryMode.Allowlist"/> accepts only registered queries (<c>AddPersistedQueries</c>), which
    /// closes the endpoint to ad-hoc queries; <see cref="PersistedQueryMode.Off"/> (default) accepts any query.
    /// </summary>
    public PersistedQueryMode Mode { get; set; } = PersistedQueryMode.Off;
}
