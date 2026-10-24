namespace Modulus.MultiTenancy.EntityFrameworkCore;

using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Authorization.Extensions;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Exceptions;
using Modulus.Core.Abstractions.Security;

/// <summary>Body of <c>POST /api/tenancy/tenants</c>: a new company's short name, its display name and its group.</summary>
/// <param name="Slug">The short name: lower-case letters, digits and hyphens, starting with a letter or digit.</param>
/// <param name="DisplayName">The name shown to people.</param>
/// <param name="GroupId">The group of companies to put the company in, if any.</param>
public sealed record TenantCreateRequest(string Slug, string? DisplayName, Guid? GroupId);

/// <summary>Body of <c>PUT /api/tenancy/tenants/{id}/status</c>.</summary>
/// <param name="Status">Trial, Active, Suspended or Closed.</param>
/// <param name="TrialEndsAt">When the trial ends; required for Trial, ignored otherwise.</param>
public sealed record TenantStatusRequest(string Status, DateTimeOffset? TrialEndsAt);

/// <summary>Body of <c>PUT /api/tenancy/tenants/{id}/seats</c>: the seat limit, or null to remove it.</summary>
/// <param name="MaxUsers">The most active members the company may have.</param>
public sealed record TenantSeatsRequest(int? MaxUsers);

/// <summary>Body of <c>PUT /api/tenancy/tenants/{id}/parent</c>: the parent company, or null to make the company top-level.</summary>
/// <param name="ParentId">The parent company's id.</param>
public sealed record TenantParentRequest(Guid? ParentId);

/// <summary>Body of <c>POST /api/tenancy/tenants/{id}/members</c>.</summary>
/// <param name="UserId">The account to make a member.</param>
public sealed record TenantMemberRequest(Guid UserId);

/// <summary>
/// The company administration API (premodule 2): list and read companies, create one, change its status and seats, and manage its members.
/// It runs in the host context (a company administrator cannot reach another company's record through it), and every route needs
/// <see cref="ManagePermission"/>, which is Critical. The rules (suspended is read-only, closed does not resolve, the seat limit) are
/// enforced by <see cref="TenantManager{TTenant,TMembership,TDataStore}"/>; these routes only map its answers to HTTP.
/// </summary>
public static partial class TenantAdminEndpoints
{
    /// <summary>The permission that guards the routes.</summary>
    public const string ManagePermission = "tenants:manage";

    private static readonly Regex SlugPattern = SlugRegex();

    /// <summary>Registers the permission. Requires <c>AddModulusAuthorization</c> for it to be enforced.</summary>
    public static IServiceCollection AddModulusTenantAdmin(this IServiceCollection services)
        => services.AddPermissions("Modulus.Tenancy.Companies", registry =>
            registry.Add(
                ManagePermission,
                "Create companies, change their status and seat limit, and manage their members.",
                null,
                PermissionSensitivity.Critical));

    /// <summary>
    /// Maps <c>GET</c> and <c>POST</c> on <paramref name="prefix"/> (default <c>/api/tenancy/tenants</c>), <c>GET {id}</c>,
    /// <c>PUT {id}/status</c>, <c>PUT {id}/seats</c>, <c>PUT {id}/parent</c>, <c>GET {id}/members</c>, <c>POST {id}/members</c> and
    /// <c>DELETE {id}/members/{userId}</c>.
    /// </summary>
    public static RouteGroupBuilder MapModulusTenantAdmin<TTenant, TMembership, TDataStore>(this IEndpointRouteBuilder endpoints, string prefix = "/api/tenancy/tenants")
        where TTenant : TenantEntity, new()
        where TMembership : TenantMembershipEntity, new()
        where TDataStore : TenantDataStoreEntity
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var group = endpoints.MapGroup(prefix).RequireAuthorization(ManagePermission);

        group.MapGet("/", async (string? search, int? page, int? pageSize, TenantManager<TTenant, TMembership, TDataStore> manager, ICurrentTenant tenant, CancellationToken ct) =>
        {
            using var _ = tenant.Change(null);
            var number = Math.Max(page ?? 1, 1);
            var size = Math.Clamp(pageSize ?? 50, 1, 200);
            var (items, total) = await manager.ListAsync(search, number, size, ct);
            return Results.Ok(new { items, total, page = number, pageSize = size });
        });

        group.MapPost("/", async (TenantCreateRequest body, TenantManager<TTenant, TMembership, TDataStore> manager, ICurrentTenant tenant, CancellationToken ct) =>
        {
            using var _ = tenant.Change(null);
            var slug = body.Slug?.Trim().ToLowerInvariant() ?? "";
            if (!SlugPattern.IsMatch(slug))
                return Problem("slug", "A company name is 1 to 63 lower-case letters, digits or hyphens, starting with a letter or digit.");
            try
            {
                var created = await manager.CreateAsync(slug, string.IsNullOrWhiteSpace(body.DisplayName) ? null : body.DisplayName.Trim(), null, body.GroupId, ct);
                var view = await manager.GetAsync(created.TenantId, ct);
                return Results.Created($"{prefix}/{created.TenantId:D}", view);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = "DUPLICATE_TENANT", message = ex.Message });
            }
        });

        group.MapGet("/{id:guid}", async (Guid id, TenantManager<TTenant, TMembership, TDataStore> manager, ICurrentTenant tenant, CancellationToken ct) =>
        {
            using var _ = tenant.Change(null);
            return await manager.GetAsync(id, ct) is { } view ? Results.Ok(view) : Results.NotFound();
        });

        group.MapPut("/{id:guid}/status", async (Guid id, TenantStatusRequest body, TenantManager<TTenant, TMembership, TDataStore> manager, ICurrentTenant tenant, CancellationToken ct) =>
        {
            using var _ = tenant.Change(null);
            if (!Enum.TryParse<TenantStatus>(body.Status, ignoreCase: true, out var status) || !Enum.IsDefined(status))
                return Problem("status", "The status is Trial, Active, Suspended or Closed.");
            try
            {
                if (!await manager.SetStatusAsync(id, status, body.TrialEndsAt, ct))
                    return Results.NotFound();
                return Results.Ok(await manager.GetAsync(id, ct));
            }
            catch (ArgumentException ex)
            {
                return Problem(ex.ParamName ?? "status", ex.Message);
            }
        });

        group.MapPut("/{id:guid}/seats", async (Guid id, TenantSeatsRequest body, TenantManager<TTenant, TMembership, TDataStore> manager, ICurrentTenant tenant, CancellationToken ct) =>
        {
            using var _ = tenant.Change(null);
            if (body.MaxUsers is < 0)
                return Problem("maxUsers", "The seat limit cannot be negative.");
            if (!await manager.SetMaxUsersAsync(id, body.MaxUsers, ct))
                return Results.NotFound();
            return Results.Ok(await manager.GetAsync(id, ct));
        });

        group.MapPut("/{id:guid}/parent", async (Guid id, TenantParentRequest body, TenantManager<TTenant, TMembership, TDataStore> manager, ICurrentTenant tenant, CancellationToken ct) =>
        {
            using var _ = tenant.Change(null);
            try
            {
                if (!await manager.SetParentAsync(id, body.ParentId, ct))
                    return Results.NotFound();
                return Results.Ok(await manager.GetAsync(id, ct));
            }
            catch (ArgumentException ex)
            {
                return Problem(ex.ParamName ?? "parentId", ex.Message);
            }
        });

        group.MapGet("/{id:guid}/members", async (Guid id, TenantManager<TTenant, TMembership, TDataStore> manager, ICurrentTenant tenant, CancellationToken ct) =>
        {
            using var _ = tenant.Change(null);
            if (await manager.GetAsync(id, ct) is null)
                return Results.NotFound();
            return Results.Ok(await manager.ListMembersAsync(id, ct));
        });

        group.MapPost("/{id:guid}/members", async (Guid id, TenantMemberRequest body, TenantManager<TTenant, TMembership, TDataStore> manager, ICurrentTenant tenant, CancellationToken ct) =>
        {
            using var _ = tenant.Change(null);
            if (body.UserId == Guid.Empty)
                return Problem("userId", "A user is required.");
            try
            {
                return await manager.AddMemberAsync(body.UserId, id, ct) ? Results.NoContent() : Results.NotFound();
            }
            catch (ConflictException ex)
            {
                return Results.Conflict(new { error = "SEAT_LIMIT", message = ex.Message });
            }
        });

        group.MapDelete("/{id:guid}/members/{userId:guid}", async (Guid id, Guid userId, TenantManager<TTenant, TMembership, TDataStore> manager, ICurrentTenant tenant, CancellationToken ct) =>
        {
            using var _ = tenant.Change(null);
            return await manager.RemoveMemberAsync(userId, id, ct) ? Results.NoContent() : Results.NotFound();
        });

        return group;
    }

    private static IResult Problem(string field, string message)
        => Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,62}$")]
    private static partial Regex SlugRegex();
}
