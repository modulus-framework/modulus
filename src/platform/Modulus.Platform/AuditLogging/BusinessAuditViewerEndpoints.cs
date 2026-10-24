using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Authorization.Extensions;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Common;

namespace Modulus.AuditLogging;

/// <summary>One business audit entry, as the viewer shows it.</summary>
public sealed record BusinessAuditView(
    Guid Id,
    DateTimeOffset OccurredAt,
    Guid? TenantId,
    Guid? UserId,
    string? UserName,
    string Action,
    string? Resource,
    string? ResourceId,
    string? Detail);

/// <summary>
/// The business audit viewer (premodule 4): search the business audit log (<see cref="IAuditLogStore"/>) by entity, actor, action and
/// time, and read one entry. A caller sees its own company's entries; the host context sees every company. A caller with no company
/// resolved sees nothing (fail closed). Every route needs <see cref="ViewPermission"/>.
/// </summary>
public static class BusinessAuditViewerEndpoints
{
    /// <summary>The permission that reads the business audit log. Sensitive: entries name the people who acted and what they changed.</summary>
    public const string ViewPermission = "audit:view";

    /// <summary>The largest page the viewer returns.</summary>
    public const int MaxPageSize = 200;

    /// <summary>
    /// Registers the permission and the business audit log store the viewer reads (the in-memory default unless a durable store was added
    /// first). Requires <c>AddModulusAuthorization</c> for the permission to be enforced.
    /// </summary>
    public static IServiceCollection AddModulusBusinessAuditViewer(this IServiceCollection services)
        => services.AddModulusAuditLogging().AddPermissions("Modulus.Audit.Business", registry =>
            registry.Add(
                ViewPermission,
                "Read the business audit log of the caller's company: search entries and read one.",
                null,
                PermissionSensitivity.Sensitive));

    /// <summary>
    /// Maps <c>GET {prefix}</c> (search: <c>entity</c>, <c>actor</c>, <c>action</c>, <c>since</c>, <c>until</c>, <c>page</c>, <c>pageSize</c>)
    /// and <c>GET {prefix}/{id}</c>. Default prefix <c>/api/audit/business</c>.
    /// </summary>
    public static RouteGroupBuilder MapModulusBusinessAuditViewer(this IEndpointRouteBuilder endpoints, string prefix = "/api/audit/business")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var group = endpoints.MapGroup(prefix).RequireAuthorization(ViewPermission);

        group.MapGet("/", async (string? entity, Guid? actor, string? action, DateTimeOffset? since, DateTimeOffset? until,
            int? page, int? pageSize, IAuditLogStore store, ICurrentTenant tenant, CancellationToken ct) =>
        {
            var number = Math.Max(page ?? 1, 1);
            var size = Math.Clamp(pageSize ?? 50, 1, MaxPageSize);
            if (!TryScope(tenant, out var companyId))
                return Results.Ok(new PagedList<BusinessAuditView> { Items = [], TotalCount = 0, Page = number, PageSize = size });

            var query = new AuditLogQuery
            {
                From = since,
                To = until,
                TenantId = companyId,
                UserId = actor,
                Action = string.IsNullOrWhiteSpace(action) ? null : action.Trim(),
                Resource = string.IsNullOrWhiteSpace(entity) ? null : entity.Trim(),
                Page = number,
                PageSize = size,
            };
            var result = await store.QueryAsync(query, ct);
            return Results.Ok(new PagedList<BusinessAuditView>
            {
                Items = result.Items.Select(View).ToList(),
                TotalCount = result.TotalCount,
                Page = result.Page,
                PageSize = result.PageSize,
            });
        });

        group.MapGet("/{id:guid}", async (Guid id, IAuditLogStore store, ICurrentTenant tenant, CancellationToken ct) =>
        {
            if (!TryScope(tenant, out var companyId))
                return Results.NotFound();
            var entry = await store.GetOrNullAsync(id, ct);
            // Another company's entry answers "not found", the same as a missing one.
            if (entry is null || (companyId is { } own && entry.TenantId != own))
                return Results.NotFound();
            return Results.Ok(View(entry));
        });

        return group;
    }

    /// <summary>
    /// The company filter for a caller: <c>null</c> (all companies) in the host context, the caller's company otherwise.
    /// Returns <see langword="false"/> when no company is resolved outside the host context, so nothing is read.
    /// </summary>
    private static bool TryScope(ICurrentTenant tenant, out Guid? companyId)
    {
        if (tenant.IsHost)
        {
            companyId = null;
            return true;
        }
        companyId = tenant.TenantId;
        return companyId is not null;
    }

    private static BusinessAuditView View(AuditLogEntry entry) => new(
        entry.Id, entry.OccurredAt, entry.TenantId, entry.UserId, entry.UserName, entry.Action, entry.Resource, entry.ResourceId, entry.Detail);
}
