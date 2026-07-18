using System.Collections.Concurrent;
using System.Linq.Expressions;
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
/// <see cref="EndpointBase"/>) and registers minimal-API routes for each.
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

    private static void RegisterEndpoint(
        IEndpointRouteBuilder app,
        Type endpointType,
        EndpointConfig config)
    {
        var builder = app.MapMethods(
            config.Route,
            [config.Verb],
            async (HttpContext ctx) =>
                await ExecuteAsync(endpointType, config, ctx));

        // Authorization
        if (config.AllowAnonymous)
        {
            builder.AllowAnonymous();
        }
        else
        {
            var authData = new List<IAuthorizeData>();

            foreach (var perm in config.Permissions)
                authData.Add(new AuthorizeAttribute(perm));

            foreach (var policy in config.Policies)
                authData.Add(new AuthorizeAttribute(policy));

            if (config.Roles.Length > 0)
                authData.Add(new AuthorizeAttribute
                {
                    Roles = string.Join(',', config.Roles)
                });

            if (authData.Count > 0)
                builder.RequireAuthorization([.. authData]);
        }

        // OpenAPI metadata
        var tag = config.Tag ?? ExtractTag(endpointType);
        builder.WithTags(tag);

        if (config.Summary is not null)
            builder.WithSummary(config.Summary);

        if (config.Deprecated)
            builder.WithDescription("[DEPRECATED] " + (config.Summary ?? ""));
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

    // ── Request execution ─────────────────────────────────────────

    /// <summary>
    /// Cached compiled delegates per endpoint type. Replaces the old
    /// <c>dynamic</c> dispatch which paid the DLR runtime-binder cost on
    /// every request and lost compile-time type safety.
    /// </summary>
    private static readonly ConcurrentDictionary<Type, Func<EndpointBase, object, CancellationToken, Task>> s_handlers = new();

    private static async Task ExecuteAsync(
        Type endpointType,
        EndpointConfig config,
        HttpContext ctx)
    {
        await using var scope = ctx.RequestServices.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var ct = ctx.RequestAborted;

        // Activate endpoint from DI
        var endpoint = (EndpointBase)ActivatorUtilities.CreateInstance(sp, endpointType);
        endpoint.Initialize(ctx);

        // Bind request
        var request = await BindRequestAsync(config.RequestType, ctx, config.Verb, ct);

        // Validate
        if (!await ValidateAsync(sp, config.RequestType, request, ctx, ct))
            return;

        // Execute handler via a cached compiled delegate (no dynamic dispatch)
        try
        {
            var handler = s_handlers.GetOrAdd(endpointType, CompileHandler);
            await handler(endpoint, request, ct);
        }
        catch (HttpResponseException ex)
        {
            if (!ctx.Response.HasStarted)
            {
                ctx.Response.StatusCode = ex.StatusCode;
                await ctx.Response.WriteAsJsonAsync(
                    ApiResponse.Fail(ex.Message, traceId: ctx.TraceIdentifier), ct);
            }
        }
    }

    /// <summary>
    /// Compiles a strongly-typed delegate for the endpoint's HandleAsync
    /// method so the per-request call avoids DLR overhead.
    /// </summary>
    private static Func<EndpointBase, object, CancellationToken, Task> CompileHandler(
        Type endpointType)
    {
        var method = endpointType.GetMethod(
            "HandleAsync", BindingFlags.Public | BindingFlags.Instance);

        if (method is null)
            throw new InvalidOperationException(
                $"Endpoint '{endpointType.FullName}' must have a public HandleAsync method.");

        var requestType = method.GetParameters()[0].ParameterType;

        var epParam = Expression.Parameter(typeof(EndpointBase), "ep");
        var reqParam = Expression.Parameter(typeof(object), "req");
        var ctParam = Expression.Parameter(typeof(CancellationToken), "ct");

        var castEp = Expression.Convert(epParam, endpointType);
        var castReq = Expression.Convert(reqParam, requestType);

        var call = Expression.Call(castEp, method, castReq, ctParam);

        return Expression.Lambda<Func<EndpointBase, object, CancellationToken, Task>>(
            call, epParam, reqParam, ctParam).Compile();
    }

    // ── Request binding ────────────────────────────────────────────

    private static async Task<object> BindRequestAsync(
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
                ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
                await ctx.Response.WriteAsJsonAsync(new ApiErrorResponse
                {
                    Message = "Malformed JSON body.",
                    TraceId = ctx.TraceIdentifier,
                }, ct);
                return request!;
            }
        }

        request ??= Activator.CreateInstance(requestType)!;

        // Overlay route values (always — route params have highest priority)
        foreach (var (key, value) in ctx.GetRouteData().Values)
        {
            if (value is not null)
                SetProperty(request, key, value.ToString()!);
        }

        // Query binding for non-body methods
        if (!IsBodyMethod(verb))
        {
            foreach (var (key, values) in ctx.Request.Query)
            {
                if (values.Count > 0)
                    SetProperty(request, key, values.ToString()!);
            }
        }

        return request;
    }

    private static bool IsBodyMethod(string verb)
        => verb is "POST" or "PUT" or "PATCH";

    private static void SetProperty(object target, string key, string value)
    {
        var prop = target.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(p => string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase)
                                 && p.CanWrite);

        if (prop is null)
            return;

        var converted = ConvertValue(value, prop.PropertyType);
        if (converted is not null)
            prop.SetValue(target, converted);
    }

    private static object? ConvertValue(string value, Type targetType)
    {
        try
        {
            var nullableType = Nullable.GetUnderlyingType(targetType);
            if (nullableType is not null)
                targetType = nullableType;

            if (targetType == typeof(string))
                return value;

            if (targetType == typeof(Guid) && Guid.TryParse(value, out var guid))
                return guid;

            if (targetType == typeof(DateTime) && DateTime.TryParse(value, out var dt))
                return dt;

            if (targetType == typeof(DateTimeOffset) && DateTimeOffset.TryParse(value, out var dto))
                return dto;

            if (targetType == typeof(TimeSpan) && TimeSpan.TryParse(value, out var ts))
                return ts;

            if (targetType.IsEnum)
                return Enum.Parse(targetType, value, ignoreCase: true);

            if (targetType == typeof(bool))
            {
                return value.ToLowerInvariant() switch
                {
                    "true" or "1" or "yes" or "on" => true,
                    _ => false
                };
            }

            return Convert.ChangeType(value, targetType);
        }
        catch
        {
            return null;
        }
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
            .Select(e => new ApiErrorDetail
            {
                Code = e.ErrorCode,
                Property = e.PropertyName,
                Message = e.ErrorMessage
            })
            .ToArray();

        ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
        await ctx.Response.WriteAsJsonAsync(
            new ApiErrorResponse
            {
                Message = "One or more validation errors occurred.",
                Errors = errors,
                TraceId = ctx.TraceIdentifier
            },
            ct);

        return false;
    }

    // ── Helpers ────────────────────────────────────────────────────
}
