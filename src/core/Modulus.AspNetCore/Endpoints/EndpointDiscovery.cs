using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Modulus.AspNetCore.Endpoints;

using FluentValidation;
using Modulus.AspNetCore.Http;

/// <summary>
/// Scans assemblies for REPR endpoints (classes inheriting from
/// <see cref="EndpointBase"/>) and maps each one as a standard minimal-API
/// route. The authoring surface stays REPR (<c>Configure()</c> + typed
/// <c>HandleAsync</c>); the engine underneath is conventional ASP.NET Core: a
/// route registered through <c>MapMethods</c> with authorization and OpenAPI
/// metadata attached as endpoint conventions, executing in the request's own
/// DI scope through a statically-typed delegate closed once at startup.
/// </summary>
public static class EndpointDiscovery
{
    /// <summary>
    /// Scans the specified assemblies and maps all discovered endpoints.
    /// Call <c>app.MapModulusEndpoints()</c> from your Program.cs.
    /// </summary>
    public static IEndpointRouteBuilder MapModulusEndpoints(
        this IEndpointRouteBuilder app,
        params Assembly[] assemblies)
    {
        if (assemblies.Length == 0)
            assemblies = [Assembly.GetCallingAssembly()];

        var endpointTypes = DiscoverEndpointTypes(assemblies, app.ServiceProvider);
        var logger = app.ServiceProvider.GetService<ILoggerFactory>()
            ?.CreateLogger("Modulus.Endpoints");

        var registered = 0;

        foreach (var (endpointType, config) in endpointTypes)
        {
            RegisterEndpoint(app, endpointType, config);
            registered++;
        }

        logger?.LogInformation(
            "Discovered and registered {Count} REPR endpoints.", registered);

        return app;
    }

    // ── Discovery ──────────────────────────────────────────────────

    private static List<(Type Type, EndpointConfig Config)> DiscoverEndpointTypes(
        Assembly[] assemblies, IServiceProvider serviceProvider)
    {
        var results = new List<(Type, EndpointConfig)>();
        var baseType = typeof(EndpointBase);

        // Use a temporary scope so scoped services (IMediator, etc.) resolve
        // correctly during discovery.  The instance is discarded after
        // Configure() is called — only the captured EndpointConfig matters.
        using var scope = serviceProvider.CreateScope();
        var sp = scope.ServiceProvider;

        foreach (var assembly in assemblies)
        {
            foreach (var type in assembly.GetTypes())
            {
                if (type.IsAbstract || type.IsInterface)
                    continue;

                if (!baseType.IsAssignableFrom(type))
                    continue;

                // Create a throwaway instance just to call Configure().
                // The real service provider is used so that constructor
                // injection works (FastEndpoints-style).
                var instance = (EndpointBase)ActivatorUtilities.CreateInstance(
                    sp, type)!;

                instance.Configure();

                // Validate route was set
                if (string.IsNullOrWhiteSpace(instance.Config.Route))
                    throw new InvalidOperationException(
                        $"Endpoint '{type.FullName}' did not configure a route. " +
                        "Call one of Get/Post/Put/Patch/Delete in Configure().");

                results.Add((type, instance.Config));
            }
        }

        return results;
    }

    // ── Route registration ────────────────────────────────────────

    private static readonly MethodInfo s_mapCore = typeof(EndpointDiscovery)
        .GetMethod(nameof(MapCore), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static void RegisterEndpoint(
        IEndpointRouteBuilder app,
        Type endpointType,
        EndpointConfig config)
    {
        if (config.RequestType is null
            || !typeof(IEndpointHandler<>).MakeGenericType(config.RequestType)
                .IsAssignableFrom(endpointType))
        {
            throw new InvalidOperationException(
                $"Endpoint '{endpointType.FullName}' must inherit " +
                "Endpoint<TRequest>, Endpoint<TRequest, TResponse>, or " +
                "EndpointWithoutRequest<TResponse>.");
        }

        // Close the typed registration once at startup; every per-request
        // concern from here on is statically typed.
        s_mapCore.MakeGenericMethod(endpointType, config.RequestType)
            .Invoke(null, [app, endpointType, config]);
    }

    private static void MapCore<TEndpoint, TRequest>(
        IEndpointRouteBuilder app,
        Type endpointType,
        EndpointConfig config)
        where TEndpoint : EndpointBase, IEndpointHandler<TRequest>
        where TRequest : class, new()
    {
        // Constructor injection without registering endpoints in DI: the
        // factory is resolved once and reused for every request.
        var factory = ActivatorUtilities.CreateFactory(typeof(TEndpoint), Type.EmptyTypes);
        var verb = config.Verb;
        var bindsRequest = typeof(TRequest) != typeof(EmptyRequest);

        // Typed as Delegate (not the implicit Func<HttpContext, Task> match) so
        // overload resolution picks the minimal-API MapMethods(Delegate) overload
        // returning RouteHandlerBuilder — a bare RequestDelegate-shaped lambda
        // binds to the RequestDelegate overload instead, which only returns
        // IEndpointConventionBuilder and has no OpenAPI metadata methods.
        Delegate handler = async (HttpContext ctx) =>
        {
            // The endpoint runs in the request's own scope (ctx.RequestServices),
            // sharing scoped services with middleware — the previous engine
            // created a nested scope, silently forking e.g. the current tenant.
            var ct = ctx.RequestAborted;
            var endpoint = (TEndpoint)factory(ctx.RequestServices, arguments: null);
            endpoint.Initialize(ctx, config);

            var request = new TRequest();
            if (bindsRequest)
            {
                // A binding failure has already written a 400 problem response —
                // the handler must never run against a half-bound request.
                var (bound, succeeded) = await BindRequestAsync(
                    typeof(TRequest), ctx, verb, ct);
                if (!succeeded)
                    return;

                request = (TRequest)bound;
                if (!await ValidateAsync(
                        ctx.RequestServices, typeof(TRequest), request, ctx, ct))
                    return;
            }

            try
            {
                await endpoint.HandleAsync(request, ct);
            }
            catch (HttpResponseException ex)
            {
                if (!ctx.Response.HasStarted)
                    await ProblemResponses.WriteAsync(ctx, ex.StatusCode, ex.Message);
            }
        };

        // One route per declared version (Versions(1, 2) → /api/v1/x and /api/v2/x). A route that already starts with
        // "/api/" carries its own version and is mapped as written.
        foreach (var route in VersionedRoutes(config))
        {
            var builder = app.MapMethods(route, [verb], handler);

            ApplyAuthorization(builder, config);
            ApplyOpenApi(builder, endpointType, config, bindsRequest);
        }
    }

    /// <summary>The route(s) an endpoint is mapped at: one per declared API version unless the route is already under <c>/api/</c>.</summary>
    internal static IReadOnlyList<string> VersionedRoutes(EndpointConfig config)
    {
        var route = config.Route;
        if (config.Versions.Length == 0 || route.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
            return [route];

        return config.Versions.Distinct().Select(version => $"/api/v{version}{route}").ToArray();
    }

    private static void ApplyAuthorization(
        IEndpointConventionBuilder builder, EndpointConfig config)
    {
        if (config.SecurityPolicy is not null)
            builder.WithMetadata(config.SecurityPolicy);

        if (config.AllowAnonymous)
        {
            if (config.Loosening is not null)
                builder.WithMetadata(config.Loosening);
            builder.AllowAnonymous();
            return;
        }

        var authData = new List<IAuthorizeData>();

        if (config.Permissions.Length > 0)
        {
            foreach (var perm in config.Permissions)
                authData.Add(new AuthorizeAttribute(perm));
        }

        foreach (var policy in config.Policies)
            authData.Add(new AuthorizeAttribute(policy));

        if (config.Roles.Length > 0)
            authData.Add(new AuthorizeAttribute
            {
                Roles = string.Join(',', config.Roles)
            });

        // Default: require authenticated user when no explicit auth settings
        if (authData.Count > 0)
            builder.RequireAuthorization([.. authData]);
        else
            builder.RequireAuthorization();
    }

    private static void ApplyOpenApi(
        RouteHandlerBuilder builder,
        Type endpointType,
        EndpointConfig config,
        bool bindsRequest)
    {
        builder.WithTags(config.Tag ?? ExtractTag(endpointType));

        if (config.Summary is not null)
            builder.WithSummary(config.Summary);

        if (config.Deprecated)
        {
            builder.WithDescription("[DEPRECATED] " + (config.Summary ?? ""));
            // The OpenAPI generator reads ObsoleteAttribute metadata to emit "deprecated": true on the operation.
            builder.WithMetadata(new ObsoleteAttribute("Deprecated endpoint."));
        }

        // Request/response shapes for OpenAPI. The response type reflects the
        // conventional success path: the (optionally wrapped) payload for
        // endpoints with a response type, 204 for those without. Binding and
        // validation failures surface as RFC 7807 validation problems.
        if (bindsRequest && verbHasBody(config.Verb))
            builder.Accepts(config.RequestType, "application/json");

        if (config.ResponseType is null)
        {
            builder.Produces(StatusCodes.Status204NoContent);
        }
        else
        {
            var responseType = config.WrapResponse
                ? typeof(ApiResponse<>).MakeGenericType(config.ResponseType)
                : config.ResponseType;
            builder.Produces(config.SuccessStatusCode, responseType);
        }

        if (bindsRequest)
            builder.ProducesValidationProblem();

        // The error contract every framework error path emits (application/problem+json, see ProblemResponses).
        if (!config.AllowAnonymous)
        {
            builder.ProducesProblem(StatusCodes.Status401Unauthorized);
            builder.ProducesProblem(StatusCodes.Status403Forbidden);
        }

        builder.ProducesProblem(StatusCodes.Status500InternalServerError);

        static bool verbHasBody(string verb) => IsBodyMethod(verb);
    }

    private static string ExtractTag(Type endpointType)
    {
        var name = endpointType.Name;
        if (name.EndsWith("Endpoint", StringComparison.OrdinalIgnoreCase))
            name = name[..^"Endpoint".Length];

        // Group by prefix (e.g., "CreateUser" → "User", "GetOrderItem" → "OrderItem")
        // Simple heuristic: strip common verbs
        foreach (var verb in s_actionPrefixes)
        {
            if (name.StartsWith(verb, StringComparison.OrdinalIgnoreCase))
            {
                name = name[verb.Length..];
                break;
            }
        }

        return name;
    }

    private static readonly string[] s_actionPrefixes =
        ["Create", "Get", "List", "Update", "Delete", "Upsert", "Search", "Find"];

    // ── Request binding ────────────────────────────────────────────

    /// <summary>
    /// Binds the request from body, route values, and query string. Returns
    /// <c>Succeeded = false</c> after writing a 400 problem response when the
    /// body is malformed JSON or any matched property fails conversion — a bad
    /// value must never be silently skipped (the previous behaviour left e.g.
    /// a malformed Guid id as <c>Guid.Empty</c> and ran the handler against
    /// the wrong key). Internal for regression tests.
    /// </summary>
    internal static async Task<(object Request, bool Succeeded)> BindRequestAsync(
        Type requestType, HttpContext ctx, string verb, CancellationToken ct)
    {
        object? request = null;

        // Body binding for POST / PUT / PATCH
        if (IsBodyMethod(verb))
        {
            try
            {
                request = await ctx.Request.ReadFromJsonAsync(requestType, ct);
            }
            catch (System.Text.Json.JsonException)
            {
                await ProblemResponses.WriteAsync(
                    ctx, StatusCodes.Status400BadRequest, "Malformed JSON body.");
                return (null!, false);
            }
            catch (NotSupportedException)
            {
                // Non-JSON Content-Type on a body method (e.g. text/plain):
                // ReadFromJsonAsync refuses with NotSupportedException. A 400,
                // not an unhandled 500.
                await ProblemResponses.WriteAsync(
                    ctx, StatusCodes.Status400BadRequest, "Expected a JSON request body.");
                return (null!, false);
            }
        }

        var binder = s_binders.GetOrAdd(requestType, static t => new RequestBinder(t));
        request ??= binder.Create();

        // Collect every conversion failure so the client sees all bad
        // parameters at once, mirroring validation-problem semantics.
        Dictionary<string, string[]>? errors = null;

        // Overlay route values (always — route params have highest priority)
        foreach (var (key, value) in ctx.GetRouteData().Values)
        {
            if (value is not null)
                BindProperty(binder, request, key, value.ToString()!, ref errors);
        }

        // Query binding for non-body methods
        if (!IsBodyMethod(verb))
        {
            foreach (var (key, values) in ctx.Request.Query)
            {
                if (values.Count > 0)
                    BindQueryProperty(binder, request, key, values, ref errors);
            }
        }

        if (errors is not null)
        {
            await ProblemResponses.WriteValidationAsync(
                ctx, errors, title: "One or more binding errors occurred.");
            return (null!, false);
        }

        return (request, true);
    }

    private static bool IsBodyMethod(string verb)
        => verb is "POST" or "PUT" or "PATCH";

    // Per request type, once: the parameterless constructor and the writable public properties by (case-insensitive) name.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, RequestBinder> s_binders = new();

    private sealed class RequestBinder
    {
        private readonly Func<object> _create;

        public RequestBinder(Type requestType)
        {
            var ctor = requestType.GetConstructor(Type.EmptyTypes);
            _create = ctor is null
                ? () => Activator.CreateInstance(requestType)!
                : System.Linq.Expressions.Expression.Lambda<Func<object>>(
                    System.Linq.Expressions.Expression.New(ctor)).Compile();

            Properties = requestType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanWrite)
                .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        }

        public Dictionary<string, PropertyInfo> Properties { get; }

        public object Create() => _create();
    }

    private static void BindProperty(
        RequestBinder binder, object target, string key, string value,
        ref Dictionary<string, string[]>? errors)
    {
        // Unknown route/query keys are simply not bound — extra query
        // parameters (tracking params etc.) are not a client error.
        if (!binder.Properties.TryGetValue(key, out var prop))
            return;

        if (TryConvertValue(value, prop.PropertyType, out var converted))
        {
            if (converted is not null)
                prop.SetValue(target, converted);
        }
        else
        {
            errors ??= new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            errors[prop.Name] =
                [$"The value '{value}' is not valid for {prop.Name}."];
        }
    }

    // A repeated or comma-free query key (?ids=1&ids=2) binds to an array or list property, one conversion per value;
    // everything else keeps the single-value rule (several values join with commas, as StringValues.ToString does).
    private static void BindQueryProperty(
        RequestBinder binder, object target, string key, Microsoft.Extensions.Primitives.StringValues values,
        ref Dictionary<string, string[]>? errors)
    {
        if (binder.Properties.TryGetValue(key, out var prop)
            && TryGetCollectionElementType(prop.PropertyType, out var element, out var asArray))
        {
            var list = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(element))!;
            foreach (var item in values)
            {
                if (item is null)
                    continue;
                if (!TryConvertValue(item, element, out var converted) || converted is null)
                {
                    errors ??= new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
                    errors[prop.Name] = [$"The value '{item}' is not valid for {prop.Name}."];
                    return;
                }

                list.Add(converted);
            }

            if (asArray)
            {
                var array = Array.CreateInstance(element, list.Count);
                list.CopyTo(array, 0);
                prop.SetValue(target, array);
            }
            else
            {
                prop.SetValue(target, list);
            }

            return;
        }

        BindProperty(binder, target, key, values.ToString(), ref errors);
    }

    private static bool TryGetCollectionElementType(Type type, out Type element, out bool asArray)
    {
        element = typeof(object);
        asArray = false;
        if (type == typeof(string))
            return false;

        if (type.IsArray && type.GetArrayRank() == 1)
        {
            element = type.GetElementType()!;
            asArray = true;
            return true;
        }

        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            if (definition == typeof(List<>) || definition == typeof(IList<>) || definition == typeof(ICollection<>)
                || definition == typeof(IEnumerable<>) || definition == typeof(IReadOnlyList<>)
                || definition == typeof(IReadOnlyCollection<>))
            {
                element = type.GetGenericArguments()[0];
                return true;
            }
        }

        return false;
    }

    private static bool TryConvertValue(
        string value, Type targetType, out object? converted)
    {
        converted = null;

        var nullableType = Nullable.GetUnderlyingType(targetType);
        if (nullableType is not null)
        {
            // An explicitly empty value clears a nullable property.
            if (value.Length == 0)
                return true;
            targetType = nullableType;
        }

        if (targetType == typeof(string))
        {
            converted = value;
            return true;
        }

        if (targetType.IsEnum)
        {
            if (!Enum.TryParse(targetType, value, ignoreCase: true, out var parsed))
                return false;
            converted = parsed;
            return true;
        }

        if (targetType == typeof(bool))
        {
            // Strict: an unrecognised token is a client error, never a
            // silent `false`.
            switch (value.ToLowerInvariant())
            {
                case "true" or "1" or "yes" or "on":
                    converted = true;
                    return true;
                case "false" or "0" or "no" or "off":
                    converted = false;
                    return true;
                default:
                    return false;
            }
        }

        // Every other supported target (Guid, DateTime, DateTimeOffset,
        // TimeSpan, the numeric types, and any custom type) is bound through
        // the same IParsable<T> convention ASP.NET Core's own minimal-API
        // parameter binding uses — not a bespoke conversion per type.
        var parseMethod = s_parsableMethods.GetOrAdd(targetType, ResolveParsableMethod);
        if (parseMethod is null)
            return false;

        var args = new object?[] { value, System.Globalization.CultureInfo.InvariantCulture, null };
        var succeeded = (bool)parseMethod.Invoke(null, args)!;
        converted = args[2];
        return succeeded;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, MethodInfo?>
        s_parsableMethods = new();

    private static readonly MethodInfo s_tryParseDefinition = typeof(EndpointDiscovery)
        .GetMethod(nameof(TryParseGeneric), BindingFlags.NonPublic | BindingFlags.Static)!;

    // Closed once per distinct target type and cached: MakeGenericMethod
    // both proves targetType implements IParsable<targetType> (it fails the
    // generic constraint otherwise) and hands back the exact static method
    // to invoke, so every later binding of that type skips reflection.
    private static MethodInfo? ResolveParsableMethod(Type targetType)
    {
        var implementsIParsable = targetType.GetInterfaces().Any(i =>
            i.IsGenericType &&
            i.GetGenericTypeDefinition() == typeof(IParsable<>) &&
            i.GetGenericArguments()[0] == targetType);

        return implementsIParsable ? s_tryParseDefinition.MakeGenericMethod(targetType) : null;
    }

    private static bool TryParseGeneric<T>(
        string value, IFormatProvider provider, out object? converted)
        where T : IParsable<T>
    {
        if (T.TryParse(value, provider, out var result))
        {
            converted = result;
            return true;
        }

        converted = null;
        return false;
    }

    // ── Validation ────────────────────────────────────────────────

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, (Type Validator, Type Context)>
        s_validationTypes = new();

    private static async Task<bool> ValidateAsync(
        IServiceProvider sp,
        Type requestType,
        object request,
        HttpContext ctx,
        CancellationToken ct)
    {
        var types = s_validationTypes.GetOrAdd(requestType, static t => (
            typeof(IValidator<>).MakeGenericType(t),
            typeof(ValidationContext<>).MakeGenericType(t)));

        if (sp.GetService(types.Validator) is not IValidator validator)
            return true;

        var context = (IValidationContext)Activator.CreateInstance(types.Context, request)!;

        var result = await validator.ValidateAsync(context, ct);

        if (result.IsValid)
            return true;

        var errors = result.Errors
            .GroupBy(e => e.PropertyName ?? string.Empty)
            .ToDictionary(
                g => g.Key,
                g => g.Select(e => e.ErrorMessage).ToArray());

        await ProblemResponses.WriteValidationAsync(ctx, errors);
        return false;
    }
}
