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

        // Auto-prepend API version prefix unless the route already has one
        var route = config.Route;
        if (config.Versions.Length > 0
            && !route.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
        {
            route = $"/api/v{config.Versions[0]}{route}";
        }

        var builder = app.MapMethods(route, [verb], handler);

        ApplyAuthorization(builder, config);
        ApplyOpenApi(builder, endpointType, config, bindsRequest);
    }

    private static void ApplyAuthorization(
        IEndpointConventionBuilder builder, EndpointConfig config)
    {
        if (config.AllowAnonymous)
        {
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
            builder.WithDescription("[DEPRECATED] " + (config.Summary ?? ""));

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
            builder.Produces(StatusCodes.Status200OK, responseType);
        }

        if (bindsRequest)
            builder.ProducesValidationProblem();

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
        }

        request ??= Activator.CreateInstance(requestType)!;

        // Collect every conversion failure so the client sees all bad
        // parameters at once, mirroring validation-problem semantics.
        Dictionary<string, string[]>? errors = null;

        // Overlay route values (always — route params have highest priority)
        foreach (var (key, value) in ctx.GetRouteData().Values)
        {
            if (value is not null)
                BindProperty(request, key, value.ToString()!, ref errors);
        }

        // Query binding for non-body methods
        if (!IsBodyMethod(verb))
        {
            foreach (var (key, values) in ctx.Request.Query)
            {
                if (values.Count > 0)
                    BindProperty(request, key, values.ToString(), ref errors);
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

    private static void BindProperty(
        object target, string key, string value,
        ref Dictionary<string, string[]>? errors)
    {
        var prop = target.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(p => string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase)
                                 && p.CanWrite);

        // Unknown route/query keys are simply not bound — extra query
        // parameters (tracking params etc.) are not a client error.
        if (prop is null)
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

    private static async Task<bool> ValidateAsync(
        IServiceProvider sp,
        Type requestType,
        object request,
        HttpContext ctx,
        CancellationToken ct)
    {
        var validatorInterface = typeof(IValidator<>).MakeGenericType(requestType);

        if (sp.GetService(validatorInterface) is not IValidator validator)
            return true;

        var contextType = typeof(ValidationContext<>).MakeGenericType(requestType);
        var context = (IValidationContext)Activator.CreateInstance(contextType, request)!;

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
