namespace Modulus.GraphQL;

using System.Reflection;
using System.Runtime.CompilerServices;
using global::GraphQL;
using global::GraphQL.DI;
using global::GraphQL.Execution;
using global::GraphQL.Types;
using global::GraphQL.Validation;
using global::GraphQL.Validation.Rules.Custom;
using GraphQLParser.AST;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>Registers the GraphQL server.</summary>
public static class GraphQLServiceCollectionExtensions
{
    /// <summary>
    /// Registers the GraphQL server (GraphQL.NET; settings from the <c>GraphQL</c> section,
    /// <see cref="ModulusGraphQLOptions"/>) with one <see cref="ModulusSchema"/> made of every
    /// <see cref="IGraphQLContributor"/>: the public ones in <paramref name="assemblies"/> (typically the modules'
    /// Presentation assemblies) and those registered with <see cref="AddGraphQLContributor{TContributor}"/>. Also: field
    /// authorization (<c>AuthorizeWithPolicy</c>, so <c>module:thing:action</c> permission policies apply), errors per
    /// <see cref="GraphQLExceptionMapper"/>, depth and complexity limits, DataLoader, introspection only when enabled,
    /// and queries resolved one field at a time unless <see cref="ModulusGraphQLOptions.ParallelQueryExecution"/>.
    /// Map the endpoint with <c>MapModulusGraphQL()</c>.
    /// </summary>
    public static IServiceCollection AddModulusGraphQL(this IServiceCollection services, IConfiguration configuration, params Assembly[] assemblies)
        => services.AddModulusGraphQL(configuration, configure: null, assemblies);

    /// <summary>
    /// <see cref="AddModulusGraphQL(IServiceCollection, IConfiguration, Assembly[])"/>, with <paramref name="configure"/>
    /// for further GraphQL.NET settings (it runs after Modulus's own, and only on the first call).
    /// </summary>
    public static IServiceCollection AddModulusGraphQL(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IGraphQLBuilder>? configure,
        params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(assemblies);

        foreach (var assembly in assemblies.Distinct())
            RegisterAssembly(services, assembly);

        if (services.Any(d => d.ServiceType == typeof(GraphQLMarker)))
            return services;
        services.AddSingleton<GraphQLMarker>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Modulus.Core.Abstractions.Security.ISecuritySurfaceContributor, GraphQLSecuritySurface>());

        services.AddOptions<ModulusGraphQLOptions>()
            .Bind(configuration.GetSection(ModulusGraphQLOptions.SectionName))
            .Validate(o => o.Path.StartsWith('/'), "GraphQL:Path must start with '/'.")
            .Validate(o => o.MaxDepth is null or > 0 && o.MaxComplexity is null or > 0 && o.ListSizeEstimate >= 1,
                "GraphQL:MaxDepth and GraphQL:MaxComplexity must be positive, GraphQL:ListSizeEstimate at least 1.")
            .Validate(o => o.MaxDocumentLength is null or > 0 && o.MaxAliases is null or > 0
                    && (o.ExecutionTimeout is null || o.ExecutionTimeout > TimeSpan.Zero),
                "GraphQL:MaxDocumentLength and GraphQL:MaxAliases must be positive, GraphQL:ExecutionTimeout greater than zero.")
            .ValidateOnStart();

        services.AddGraphQL(graphql =>
        {
            graphql
                .AddSchema<ModulusSchema>()
                .AddSystemTextJson()
                .AddAuthorizationRule()
                .AddDataLoader()
                .AddErrorInfoProvider(sp => new ModulusErrorInfoProvider(new ErrorInfoProviderOptions
                {
                    ExposeExceptionDetails = Options(sp).ExposeExceptionDetails,
                }))
                .AddUnhandledExceptionHandler(LogUnhandled)
                .AddComplexityAnalyzer((complexity, sp) =>
                {
                    var options = Options(sp);
                    complexity.MaxDepth = options.MaxDepth;
                    complexity.MaxComplexity = options.MaxComplexity;
                    complexity.DefaultListImpactMultiplier = options.ListSizeEstimate;
                })
                .AddValidationRule<IntrospectionGateRule>()
                .AddValidationRule<RequestLimitsRule>()
                .ConfigureExecution(async (executionOptions, next) =>
                {
                    var settings = Options(executionOptions.RequestServices ?? throw new InvalidOperationException("GraphQL needs request services."));
                    if (PersistedQueryGate.Apply(executionOptions, settings, executionOptions.RequestServices.GetService<IPersistedQueryStore>()) is { } refused)
                        return refused;

                    using var activity = GraphQLTelemetry.Source.StartActivity("graphql.execute");
                    activity?.SetTag("graphql.operation.name", executionOptions.OperationName);

                    // Cancel the resolvers (they receive this token) when the request outlives the limit.
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(executionOptions.CancellationToken);
                    if (settings.ExecutionTimeout is { } limit)
                    {
                        timeout.CancelAfter(limit);
                        executionOptions.CancellationToken = timeout.Token;
                    }

                    var result = await next(executionOptions);
                    if (activity is not null)
                    {
                        activity.SetTag("graphql.operation.type", result.Operation?.Operation.ToString().ToLowerInvariant());
                        var errors = result.Executed ? result.Errors?.Count ?? 0 : 0;
                        activity.SetTag("graphql.errors", errors);
                        if (errors > 0)
                            activity.SetStatus(System.Diagnostics.ActivityStatusCode.Error);
                    }

                    return result;
                })
                .AddExecutionStrategy<IExecutionStrategy>(
                    sp => Options(sp).ParallelQueryExecution ? ParallelExecutionStrategy.Instance : SerialExecutionStrategy.Instance,
                    OperationType.Query);
            configure?.Invoke(graphql);
        });
        return services;
    }

    /// <summary>
    /// Adds <typeparamref name="TContributor"/>'s fields to the schema (call it from a module's
    /// <c>ConfigureServices</c>), and registers the graph types of its assembly.
    /// </summary>
    public static IServiceCollection AddGraphQLContributor<TContributor>(this IServiceCollection services)
        where TContributor : class, IGraphQLContributor
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IGraphQLContributor, TContributor>());
        RegisterGraphTypes(services, typeof(TContributor).Assembly);
        return services;
    }

    /// <summary>
    /// Lets one module add fields to a graph type another module owns, e.g. an Inventory module adding <c>stock</c> to
    /// Catalog's <c>Product</c> (resolve it through a DataLoader, <c>context.LoadBatch(...)</c>, so a list of products
    /// costs one query). <paramref name="extend"/> runs once, when the schema creates the type; extensions apply in
    /// registration order. The type must be registered by <c>AddModulusGraphQL</c> or <c>AddGraphQLContributor</c>.
    /// </summary>
    public static IServiceCollection ExtendGraphType<TGraphType>(this IServiceCollection services, Action<TGraphType> extend)
        where TGraphType : class, IGraphType
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(extend);
        services.AddSingleton(new GraphTypeExtension(typeof(TGraphType), type => extend((TGraphType)type)));
        return services;
    }

    internal static ModulusGraphQLOptions Options(IServiceProvider services)
        => services.GetRequiredService<IOptions<ModulusGraphQLOptions>>().Value;

    private static void RegisterAssembly(IServiceCollection services, Assembly assembly)
    {
        foreach (var type in ConcreteTypes(assembly).Where(t => t.IsVisible && typeof(IGraphQLContributor).IsAssignableFrom(t)))
            services.TryAddEnumerable(ServiceDescriptor.Singleton(typeof(IGraphQLContributor), type));
        RegisterGraphTypes(services, assembly);
    }

    // Graph types are created through DI (GraphQL.NET resolves every type it references from the container); the
    // factory applies the extensions other modules registered for the type.
    private static void RegisterGraphTypes(IServiceCollection services, Assembly assembly)
    {
        foreach (var type in ConcreteTypes(assembly).Where(t => typeof(IGraphType).IsAssignableFrom(t)))
        {
            if (services.Any(d => d.ServiceType == type))
                continue;
            services.AddTransient(type, sp =>
            {
                var instance = ActivatorUtilities.CreateInstance(sp, type);
                foreach (var extension in sp.GetServices<GraphTypeExtension>().Where(e => e.GraphType == type))
                    extension.Apply(instance);
                return instance;
            });
        }
    }

    private static IEnumerable<Type> ConcreteTypes(Assembly assembly)
    {
        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types.OfType<Type>().ToArray();
        }

        return types
            .Where(t => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false }
                        && !t.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false))
            .OrderBy(t => t.FullName, StringComparer.Ordinal);
    }

    // Client errors (validation, not found, ...) are expected: Warning, as GlobalExceptionHandler logs them.
    private static void LogUnhandled(UnhandledExceptionContext context)
    {
        var logger = context.ExecutionOptions.RequestServices?.GetService<ILoggerFactory>()?.CreateLogger("Modulus.GraphQL");
        if (logger is null)
            return;
        var exception = context.OriginalException;
        var field = context.FieldContext?.FieldDefinition.Name;
        if (GraphQLExceptionMapper.Map(exception).IsClientError)
            logger.LogWarning("GraphQL client error in {Field}: {Type}: {Message}", field, exception.GetType().Name, exception.Message);
        else
            logger.LogError(exception, "GraphQL resolver error in {Field}: {Type}", field, exception.GetType().Name);
    }

    private sealed class GraphQLMarker;
}

/// <summary>Rejects <c>__schema</c> / <c>__type</c> unless <see cref="ModulusGraphQLOptions.EnableIntrospection"/>.</summary>
internal sealed class IntrospectionGateRule(IOptions<ModulusGraphQLOptions> options) : ValidationRuleBase
{
    public override ValueTask<INodeVisitor?> GetPreNodeVisitorAsync(ValidationContext context)
        => options.Value.EnableIntrospection ? default : NoIntrospectionValidationRule.Instance.GetPreNodeVisitorAsync(context);
}

/// <summary>The activity source for GraphQL operations (<c>Modulus.GraphQL</c>), listened to by the Modulus OpenTelemetry setup.</summary>
internal static class GraphQLTelemetry
{
    public static readonly System.Diagnostics.ActivitySource Source = new("Modulus.GraphQL", "1.0.0");
}

/// <summary>Rejects a document longer than <see cref="ModulusGraphQLOptions.MaxDocumentLength"/> or with more aliases than <see cref="ModulusGraphQLOptions.MaxAliases"/>.</summary>
internal sealed class RequestLimitsRule(IOptions<ModulusGraphQLOptions> options) : ValidationRuleBase
{
    public override ValueTask<INodeVisitor?> GetPreNodeVisitorAsync(ValidationContext context)
    {
        var settings = options.Value;
        if (settings.MaxDocumentLength is { } maxLength && context.Document.Source.Length > maxLength)
        {
            context.ReportError(new ValidationError(
                context.Document.Source, "limits", $"The query is longer than the {maxLength} characters allowed."));
            return default;
        }

        if (settings.MaxAliases is not { } maxAliases)
            return default;

        var aliases = 0;
        return new ValueTask<INodeVisitor?>(new MatchingNodeVisitor<GraphQLField>((field, ctx) =>
        {
            if (field.Alias is not null && ++aliases == maxAliases + 1)
                ctx.ReportError(new ValidationError(
                    ctx.Document.Source, "limits", $"The query uses more than the {maxAliases} aliases allowed.", field));
        }));
    }
}
