using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Modulus.Core.Abstractions;
using Modulus.Authorization.Extensions;
using Modulus.Identity.Abstractions;

namespace Modulus.Identity.Scim;

/// <summary>SCIM 2.0 (RFC 7643/7644) provisioning of users and groups, for HR systems and identity providers.</summary>
public static class ScimExtensions
{
    /// <summary>The permission that guards every SCIM endpoint. Grant it to the integration client's role (<see cref="IntegrationClients.RoleFor"/>).</summary>
    public const string ManagePermission = "scim:manage";

    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Registers the SCIM service over the Identity user and role managers (settings <c>Identity:Scim</c>,
    /// <see cref="ScimOptions"/>) and declares <see cref="ManagePermission"/>. Call after <c>AddModulusIdentity</c>; map the
    /// endpoints with <see cref="MapModulusScim"/>.
    /// <para>
    /// The caller is an integration client bound to a company (its token carries <c>tid</c>); everything happens in that
    /// company. A <c>ITenantMemberProvisioner</c> (registered by <c>AddEfCoreTenantStore</c>) makes the company's seat limit
    /// apply: creating or re-activating an account takes a seat, deactivating or deleting it frees one.
    /// </para>
    /// </summary>
    public static IServiceCollection AddModulusScim<TUser>(this IServiceCollection services, IConfiguration configuration)
        where TUser : ModulusUser, new()
        => services.AddModulusScim<TUser, ModulusRole>(configuration);

    /// <summary>
    /// Registers the SCIM service for an application's own user and role classes (derived from <see cref="ModulusUser"/> and
    /// <see cref="ModulusRole"/>), so the groups it manages are the application's extended roles. Otherwise as
    /// <see cref="AddModulusScim{TUser}"/>.
    /// </summary>
    public static IServiceCollection AddModulusScim<TUser, TRole>(this IServiceCollection services, IConfiguration configuration)
        where TUser : ModulusUser, new()
        where TRole : ModulusRole, new()
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.Configure<ScimOptions>(configuration.GetSection("Identity:Scim"));
        services.TryAddScoped<IScimService, ScimService<TUser, TRole>>();
        return services.AddPermissions("Modulus.Identity.Scim", registry =>
            registry.Add(
                ManagePermission,
                "Provision users and groups of the company through SCIM (create, change, deactivate accounts, set role membership).",
                null,
                PermissionSensitivity.Critical));
    }

    /// <summary>Maps <c>{prefix}/Users</c>, <c>/Groups</c> and the discovery documents (default prefix <c>/scim/v2</c>), guarded by <see cref="ManagePermission"/>.</summary>
    public static RouteGroupBuilder MapModulusScim(this IEndpointRouteBuilder endpoints, string prefix = "/scim/v2")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var group = endpoints.MapGroup(prefix).RequireAuthorization(ManagePermission);
        group.AddEndpointFilter(async (context, next) =>
        {
            try
            {
                return await next(context);
            }
            catch (ScimException ex)
            {
                return Error(ex.Status, ex.ScimType, ex.Message);
            }
        });

        MapUsers(group, prefix);
        MapGroups(group, prefix);
        MapBulk(group, prefix);
        MapDiscovery(group, prefix);
        return group;
    }

    private static string BaseUrl(HttpRequest request, string prefix) => $"{request.Scheme}://{request.Host}{request.PathBase}{prefix}";

    private static IResult Scim(object body, int status = StatusCodes.Status200OK)
        => Results.Json(body, s_json, ScimMediaTypes.Json, status);

    private static IResult Error(int status, string? scimType, string detail)
        => Scim(new ScimError { Status = status.ToString(System.Globalization.CultureInfo.InvariantCulture), ScimType = scimType, Detail = detail }, status);

    private static IResult NotFound(string kind) => Error(404, null, $"{kind} not found.");

    // RFC 7644 §3.14: the If-Match tag of a change, or null when the client sent none. The service checks it against the record.
    private static string? IfMatch(HttpRequest request)
    {
        var value = request.Headers["If-Match"].ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static void MapUsers(RouteGroupBuilder group, string prefix)
    {
        group.MapGet("/Users", async (HttpRequest request, IScimService scim, string? filter, string? sortBy, string? sortOrder, int? startIndex, int? count, CancellationToken ct) =>
            Scim(await scim.ListUsersAsync(filter, sortBy, sortOrder, startIndex ?? 1, count ?? 100, BaseUrl(request, prefix), ct)));

        group.MapGet("/Users/{id}", async (HttpRequest request, IScimService scim, string id, CancellationToken ct) =>
            await scim.GetUserAsync(id, BaseUrl(request, prefix), ct) is { } user
                ? WithETag(Scim(user), user.Meta?.Version)
                : NotFound("User"));

        group.MapPost("/Users", async (HttpRequest request, IScimService scim, ScimUser body, CancellationToken ct) =>
        {
            var user = await scim.CreateUserAsync(body, BaseUrl(request, prefix), ct);
            return WithETag(
                Results.Json(user, s_json, ScimMediaTypes.Json, StatusCodes.Status201Created).WithLocation(user.Meta?.Location),
                user.Meta?.Version);
        });

        group.MapPut("/Users/{id}", async (HttpRequest request, IScimService scim, string id, ScimUser body, CancellationToken ct) =>
            await scim.ReplaceUserAsync(id, body, BaseUrl(request, prefix), IfMatch(request), ct) is { } user
                ? WithETag(Scim(user), user.Meta?.Version)
                : NotFound("User"));

        group.MapPatch("/Users/{id}", async (HttpRequest request, IScimService scim, string id, ScimPatchRequest body, CancellationToken ct) =>
            await scim.PatchUserAsync(id, body, BaseUrl(request, prefix), IfMatch(request), ct) is { } user
                ? WithETag(Scim(user), user.Meta?.Version)
                : NotFound("User"));

        group.MapDelete("/Users/{id}", async (HttpRequest request, IScimService scim, string id, CancellationToken ct) =>
            await scim.DeleteUserAsync(id, IfMatch(request), ct) ? Results.NoContent() : NotFound("User"));
    }

    private static void MapGroups(RouteGroupBuilder group, string prefix)
    {
        group.MapGet("/Groups", async (HttpRequest request, IScimService scim, string? filter, string? sortBy, string? sortOrder, int? startIndex, int? count, CancellationToken ct) =>
            Scim(await scim.ListGroupsAsync(filter, sortBy, sortOrder, startIndex ?? 1, count ?? 100, BaseUrl(request, prefix), ct)));

        group.MapGet("/Groups/{id}", async (HttpRequest request, IScimService scim, string id, CancellationToken ct) =>
            await scim.GetGroupAsync(id, BaseUrl(request, prefix), ct) is { } found
                ? WithETag(Scim(found), found.Meta?.Version)
                : NotFound("Group"));

        group.MapPost("/Groups", async (HttpRequest request, IScimService scim, ScimGroup body, CancellationToken ct) =>
        {
            var created = await scim.CreateGroupAsync(body, BaseUrl(request, prefix), ct);
            return WithETag(
                Results.Json(created, s_json, ScimMediaTypes.Json, StatusCodes.Status201Created).WithLocation(created.Meta?.Location),
                created.Meta?.Version);
        });

        group.MapPut("/Groups/{id}", async (HttpRequest request, IScimService scim, string id, ScimGroup body, CancellationToken ct) =>
            await scim.ReplaceGroupAsync(id, body, BaseUrl(request, prefix), IfMatch(request), ct) is { } found
                ? WithETag(Scim(found), found.Meta?.Version)
                : NotFound("Group"));

        group.MapPatch("/Groups/{id}", async (HttpRequest request, IScimService scim, string id, ScimPatchRequest body, CancellationToken ct) =>
            await scim.PatchGroupAsync(id, body, BaseUrl(request, prefix), IfMatch(request), ct) is { } found
                ? WithETag(Scim(found), found.Meta?.Version)
                : NotFound("Group"));

        group.MapDelete("/Groups/{id}", async (HttpRequest request, IScimService scim, string id, CancellationToken ct) =>
            await scim.DeleteGroupAsync(id, IfMatch(request), ct) ? Results.NoContent() : NotFound("Group"));
    }

    /// <summary>The limits advertised in ServiceProviderConfig and enforced by <c>POST /Bulk</c>.</summary>
    private const int MaxBulkOperations = 1000;
    private const int MaxBulkPayloadBytes = 1_048_576;

    private static readonly string[] BulkResponseSchemas = ["urn:ietf:params:scim:api:messages:2.0:BulkResponse"];

    private sealed record BulkOutcome(string Method, string? BulkId, string? Location, string Status, string? Detail);

    // RFC 7644 §3.7: operations are applied in order, each through the same service as its single-resource endpoint, so the
    // same rules, seat limits and audit apply. The bulk is not one transaction (the RFC does not require it).
    private static void MapBulk(RouteGroupBuilder group, string prefix)
    {
        group.MapPost("/Bulk", async (HttpRequest request, IScimService scim, ScimBulkRequest body, CancellationToken ct) =>
        {
            var operations = body.Operations ?? [];
            if (operations.Count > MaxBulkOperations)
                return Error(StatusCodes.Status413PayloadTooLarge, "tooMany", $"A bulk request holds at most {MaxBulkOperations} operations.");

            var baseUrl = BaseUrl(request, prefix);
            var created = new Dictionary<string, string>(StringComparer.Ordinal);
            var outcomes = new List<BulkOutcome>();
            var failures = 0;
            foreach (var operation in operations)
            {
                if (body.FailOnErrors is { } limit && failures >= limit)
                    break;
                var outcome = await RunBulkOperationAsync(operation, created, scim, baseUrl, ct);
                outcomes.Add(outcome);
                if (int.TryParse(outcome.Status, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var status) && status >= 400)
                    failures++;
            }

            return Scim(new
            {
                schemas = BulkResponseSchemas,
                Operations = outcomes,
            });
        });
    }

    private static async Task<BulkOutcome> RunBulkOperationAsync(
        ScimBulkOperation operation, Dictionary<string, string> created, IScimService scim, string baseUrl, CancellationToken ct)
    {
        var method = operation.Method?.ToUpperInvariant() ?? string.Empty;
        try
        {
            var (kind, id) = BulkPath(operation.Path, created);
            var data = operation.Data is { ValueKind: not System.Text.Json.JsonValueKind.Undefined } json
                ? ResolveBulkIds(json.GetRawText(), created)
                : null;

            if (kind == "Users")
                return await RunUserOperationAsync(method, id, data, operation.BulkId, created, scim, baseUrl, ct);
            return await RunGroupOperationAsync(method, id, data, operation.BulkId, created, scim, baseUrl, ct);
        }
        catch (ScimException ex)
        {
            return new BulkOutcome(method, operation.BulkId, null, ex.Status.ToString(System.Globalization.CultureInfo.InvariantCulture), ex.Message);
        }
    }

    private static async Task<BulkOutcome> RunUserOperationAsync(
        string method, string? id, string? data, string? bulkId, Dictionary<string, string> created, IScimService scim, string baseUrl, CancellationToken ct)
    {
        switch (method)
        {
            case "POST":
                {
                    var user = await scim.CreateUserAsync(BulkBody<ScimUser>(data), baseUrl, ct);
                    if (bulkId is not null)
                        created[bulkId] = user.Id!;
                    return new BulkOutcome(method, bulkId, user.Meta?.Location, "201", null);
                }
            case "PUT":
                {
                    var user = await scim.ReplaceUserAsync(Required(id), BulkBody<ScimUser>(data), baseUrl, null, ct);
                    return user is null ? Missing(method, bulkId) : new BulkOutcome(method, bulkId, user.Meta?.Location, "200", null);
                }
            case "PATCH":
                {
                    var user = await scim.PatchUserAsync(Required(id), BulkBody<ScimPatchRequest>(data), baseUrl, null, ct);
                    return user is null ? Missing(method, bulkId) : new BulkOutcome(method, bulkId, user.Meta?.Location, "200", null);
                }
            case "DELETE":
                return await scim.DeleteUserAsync(Required(id), null, ct)
                    ? new BulkOutcome(method, bulkId, null, "204", null)
                    : Missing(method, bulkId);
            default:
                throw new ScimException(400, "invalidSyntax", "The method must be POST, PUT, PATCH or DELETE.");
        }
    }

    private static async Task<BulkOutcome> RunGroupOperationAsync(
        string method, string? id, string? data, string? bulkId, Dictionary<string, string> created, IScimService scim, string baseUrl, CancellationToken ct)
    {
        switch (method)
        {
            case "POST":
                {
                    var group = await scim.CreateGroupAsync(BulkBody<ScimGroup>(data), baseUrl, ct);
                    if (bulkId is not null)
                        created[bulkId] = group.Id!;
                    return new BulkOutcome(method, bulkId, group.Meta?.Location, "201", null);
                }
            case "PUT":
                {
                    var group = await scim.ReplaceGroupAsync(Required(id), BulkBody<ScimGroup>(data), baseUrl, null, ct);
                    return group is null ? Missing(method, bulkId) : new BulkOutcome(method, bulkId, group.Meta?.Location, "200", null);
                }
            case "PATCH":
                {
                    var group = await scim.PatchGroupAsync(Required(id), BulkBody<ScimPatchRequest>(data), baseUrl, null, ct);
                    return group is null ? Missing(method, bulkId) : new BulkOutcome(method, bulkId, group.Meta?.Location, "200", null);
                }
            case "DELETE":
                return await scim.DeleteGroupAsync(Required(id), null, ct)
                    ? new BulkOutcome(method, bulkId, null, "204", null)
                    : Missing(method, bulkId);
            default:
                throw new ScimException(400, "invalidSyntax", "The method must be POST, PUT, PATCH or DELETE.");
        }
    }

    private static BulkOutcome Missing(string method, string? bulkId)
        => new(method, bulkId, null, "404", "The resource was not found.");

    // A path is /Users, /Groups, /Users/{id} or /Groups/{id}; {id} may be a bulkId reference created earlier in the request.
    private static (string Kind, string? Id) BulkPath(string? path, IReadOnlyDictionary<string, string> created)
    {
        var parts = (path ?? string.Empty).Trim('/').Split('/', 2);
        if (parts[0] != "Users" && parts[0] != "Groups")
            throw new ScimException(400, "invalidPath", "The path must start with /Users or /Groups.");
        if (parts.Length == 1)
            return (parts[0], null);

        var id = parts[1];
        if (id.StartsWith("bulkId:", StringComparison.Ordinal))
        {
            var bulkId = id["bulkId:".Length..];
            id = created.TryGetValue(bulkId, out var real)
                ? real
                : throw new ScimException(400, "invalidValue", "The path names a bulkId that no earlier operation created.");
        }
        return (parts[0], id);
    }

    // References to created resources inside data are written as the JSON string "bulkId:{id}".
    private static string ResolveBulkIds(string raw, IReadOnlyDictionary<string, string> created)
    {
        foreach (var (bulkId, realId) in created)
            raw = raw.Replace($"\"bulkId:{bulkId}\"", $"\"{realId}\"", StringComparison.Ordinal);
        return raw;
    }

    private static string Required(string? id)
        => string.IsNullOrEmpty(id) ? throw new ScimException(400, "invalidPath", "The operation needs the id of a resource.") : id;

    private static T BulkBody<T>(string? data) where T : class
    {
        if (data is null)
            throw new ScimException(400, "invalidSyntax", "The operation needs its data.");
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<T>(data, s_json)
                ?? throw new ScimException(400, "invalidSyntax", "The operation's data is empty.");
        }
        catch (System.Text.Json.JsonException)
        {
            throw new ScimException(400, "invalidSyntax", "The operation's data is not valid SCIM JSON.");
        }
    }

    private static void MapDiscovery(RouteGroupBuilder group, string prefix)
    {
        // RFC 7644 §4 and RFC 7643 §6: the schemas this server accepts and returns.
        group.MapGet("/Schemas", (HttpRequest request) =>
        {
            var documents = ScimSchemaDocuments.All(BaseUrl(request, prefix));
            return Scim(new
            {
                schemas = new[] { ScimSchemas.ListResponse },
                totalResults = documents.Count,
                startIndex = 1,
                itemsPerPage = documents.Count,
                Resources = documents,
            });
        });

        group.MapGet("/Schemas/{id}", (HttpRequest request, string id) =>
            ScimSchemaDocuments.Find(id, BaseUrl(request, prefix)) is { } document
                ? Scim(document)
                : NotFound("Schema"));

        group.MapGet("/ServiceProviderConfig", (Microsoft.Extensions.Options.IOptions<ScimOptions> options) => Scim(new
        {
            schemas = new[] { ScimSchemas.ServiceProviderConfig },
            patch = new { supported = true },
            bulk = new { supported = true, maxOperations = MaxBulkOperations, maxPayloadSize = MaxBulkPayloadBytes },
            filter = new { supported = true, maxResults = options.Value.MaxPageSize },
            changePassword = new { supported = false },
            sort = new { supported = true },
            etag = new { supported = true },
            authenticationSchemes = new[]
            {
                new { type = "oauthbearertoken", name = "OAuth Bearer Token", description = "An access token of an integration client bound to a company.", primary = true },
            },
        }));

        group.MapGet("/ResourceTypes", () => Scim(new
        {
            schemas = new[] { ScimSchemas.ListResponse },
            totalResults = 2,
            Resources = new[]
            {
                new { schemas = new[] { ScimSchemas.ResourceType }, id = "User", name = "User", endpoint = "/Users", schema = ScimSchemas.User },
                new { schemas = new[] { ScimSchemas.ResourceType }, id = "Group", name = "Group", endpoint = "/Groups", schema = ScimSchemas.Group },
            },
        }));
    }

    private static IResult WithLocation(this IResult result, string? location)
        => location is null ? result : new HeaderResult(result, "Location", location);

    private static IResult WithETag(IResult result, string? version)
        => version is null ? result : new HeaderResult(result, "ETag", version);

    private sealed class HeaderResult(IResult inner, string name, string value) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.Headers[name] = value;
            return inner.ExecuteAsync(httpContext);
        }
    }
}
