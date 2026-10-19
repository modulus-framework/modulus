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
        MapDiscovery(group);
        return group;
    }

    private static string BaseUrl(HttpRequest request, string prefix) => $"{request.Scheme}://{request.Host}{request.PathBase}{prefix}";

    private static IResult Scim(object body, int status = StatusCodes.Status200OK)
        => Results.Json(body, s_json, ScimMediaTypes.Json, status);

    private static IResult Error(int status, string? scimType, string detail)
        => Scim(new ScimError { Status = status.ToString(System.Globalization.CultureInfo.InvariantCulture), ScimType = scimType, Detail = detail }, status);

    private static IResult NotFound(string kind) => Error(404, null, $"{kind} not found.");

    private static void MapUsers(RouteGroupBuilder group, string prefix)
    {
        group.MapGet("/Users", async (HttpRequest request, IScimService scim, string? filter, int? startIndex, int? count, CancellationToken ct) =>
            Scim(await scim.ListUsersAsync(filter, startIndex ?? 1, count ?? 100, BaseUrl(request, prefix), ct)));

        group.MapGet("/Users/{id}", async (HttpRequest request, IScimService scim, string id, CancellationToken ct) =>
            await scim.GetUserAsync(id, BaseUrl(request, prefix), ct) is { } user ? Scim(user) : NotFound("User"));

        group.MapPost("/Users", async (HttpRequest request, IScimService scim, ScimUser body, CancellationToken ct) =>
        {
            var user = await scim.CreateUserAsync(body, BaseUrl(request, prefix), ct);
            return Results.Json(user, s_json, ScimMediaTypes.Json, StatusCodes.Status201Created)
                .WithLocation(user.Meta?.Location);
        });

        group.MapPut("/Users/{id}", async (HttpRequest request, IScimService scim, string id, ScimUser body, CancellationToken ct) =>
            await scim.ReplaceUserAsync(id, body, BaseUrl(request, prefix), ct) is { } user ? Scim(user) : NotFound("User"));

        group.MapPatch("/Users/{id}", async (HttpRequest request, IScimService scim, string id, ScimPatchRequest body, CancellationToken ct) =>
            await scim.PatchUserAsync(id, body, BaseUrl(request, prefix), ct) is { } user ? Scim(user) : NotFound("User"));

        group.MapDelete("/Users/{id}", async (IScimService scim, string id, CancellationToken ct) =>
            await scim.DeleteUserAsync(id, ct) ? Results.NoContent() : NotFound("User"));
    }

    private static void MapGroups(RouteGroupBuilder group, string prefix)
    {
        group.MapGet("/Groups", async (HttpRequest request, IScimService scim, string? filter, int? startIndex, int? count, CancellationToken ct) =>
            Scim(await scim.ListGroupsAsync(filter, startIndex ?? 1, count ?? 100, BaseUrl(request, prefix), ct)));

        group.MapGet("/Groups/{id}", async (HttpRequest request, IScimService scim, string id, CancellationToken ct) =>
            await scim.GetGroupAsync(id, BaseUrl(request, prefix), ct) is { } found ? Scim(found) : NotFound("Group"));

        group.MapPost("/Groups", async (HttpRequest request, IScimService scim, ScimGroup body, CancellationToken ct) =>
        {
            var created = await scim.CreateGroupAsync(body, BaseUrl(request, prefix), ct);
            return Results.Json(created, s_json, ScimMediaTypes.Json, StatusCodes.Status201Created)
                .WithLocation(created.Meta?.Location);
        });

        group.MapPut("/Groups/{id}", async (HttpRequest request, IScimService scim, string id, ScimGroup body, CancellationToken ct) =>
            await scim.ReplaceGroupAsync(id, body, BaseUrl(request, prefix), ct) is { } found ? Scim(found) : NotFound("Group"));

        group.MapPatch("/Groups/{id}", async (HttpRequest request, IScimService scim, string id, ScimPatchRequest body, CancellationToken ct) =>
            await scim.PatchGroupAsync(id, body, BaseUrl(request, prefix), ct) is { } found ? Scim(found) : NotFound("Group"));

        group.MapDelete("/Groups/{id}", async (IScimService scim, string id, CancellationToken ct) =>
            await scim.DeleteGroupAsync(id, ct) ? Results.NoContent() : NotFound("Group"));
    }

    private static void MapDiscovery(RouteGroupBuilder group)
    {
        group.MapGet("/ServiceProviderConfig", (Microsoft.Extensions.Options.IOptions<ScimOptions> options) => Scim(new
        {
            schemas = new[] { ScimSchemas.ServiceProviderConfig },
            patch = new { supported = true },
            bulk = new { supported = false, maxOperations = 0, maxPayloadSize = 0 },
            filter = new { supported = true, maxResults = options.Value.MaxPageSize },
            changePassword = new { supported = false },
            sort = new { supported = false },
            etag = new { supported = false },
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
        => location is null ? result : new LocationResult(result, location);

    private sealed class LocationResult(IResult inner, string location) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.Headers.Location = location;
            return inner.ExecuteAsync(httpContext);
        }
    }
}
