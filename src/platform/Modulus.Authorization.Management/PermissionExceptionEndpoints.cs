using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Modulus.Authorization.Audit;
using Modulus.Authorization.EntityFrameworkCore;
using Modulus.Authorization.Governance;
using Modulus.Authorization.Grants;
using Modulus.Core.Abstractions;

namespace Modulus.Authorization.Management;

/// <summary>Limits for the permission-exception register.</summary>
public sealed class PermissionExceptionOptions
{
    /// <summary>The longest an exception may run from the day it is recorded. Default 90 days.</summary>
    public TimeSpan MaxDuration { get; set; } = TimeSpan.FromDays(90);

    /// <summary>The shortest justification accepted. Default 20 characters.</summary>
    public int MinJustificationLength { get; set; } = 20;
}

/// <summary>An exception to record. Holder type is <c>Role</c> or <c>User</c>.</summary>
public sealed record PermissionExceptionWriteRequest(
    string? Permission, string? HolderType, string? Holder, string? Justification, string? CompensatingControl,
    string? BusinessOwner, DateTimeOffset? ValidUntil, DateTimeOffset? ReviewDueOn);

/// <summary>One exception as the API shows it.</summary>
public sealed record PermissionExceptionView(
    Guid Id, string Permission, string HolderType, string Holder, string Justification, string? CompensatingControl,
    string BusinessOwner, Guid RequestedBy, DateTimeOffset ValidFrom, DateTimeOffset ValidUntil, DateTimeOffset ReviewDueOn,
    DateTimeOffset CreatedAt, Guid? ReviewedBy, DateTimeOffset? ReviewedAt, Guid? RevokedBy, DateTimeOffset? RevokedAt, bool Active);

/// <summary>A privileged grant that no active exception explains.</summary>
public sealed record PermissionExceptionFindingView(string Permission, string HolderType, string Holder, bool HadExpiredException);

/// <summary>The permission-exception register endpoints.</summary>
public static class PermissionExceptionEndpoints
{
    /// <summary>
    /// Maps the register under <paramref name="prefix"/> (default <c>/authorization/exceptions</c>). Every route needs
    /// <see cref="AuthorizationManagementExtensions.ManagePermission"/>. An exception covers one Critical permission and one holder,
    /// has a justification, a business owner, an end date no further away than <see cref="PermissionExceptionOptions.MaxDuration"/>,
    /// and a review. Whoever records or revokes it, the review is by someone else. <c>GET findings</c> lists the privileged grants
    /// that no active exception explains. The register reports; the grants give and take access.
    /// </summary>
    public static RouteGroupBuilder MapModulusPermissionExceptions(
        this IEndpointRouteBuilder endpoints, string prefix = "/authorization/exceptions")
    {
        var group = endpoints.MapGroup(prefix).RequireAuthorization(AuthorizationManagementExtensions.ManagePermission);

        group.MapPost("/", async (
            PermissionExceptionWriteRequest request, ClaimsPrincipal caller, EfPermissionExceptionStore exceptions,
            IPermissionRegistry registry, IOptions<PermissionExceptionOptions> options, TimeProvider clock,
            IAuthorizationAuditWriter auditWriter, ICurrentUser currentUser, CancellationToken ct) =>
        {
            if (GrantGuards.UserIdOf(caller) is not { } requester)
                return Results.Unauthorized();
            var now = clock.GetUtcNow();
            var limits = options.Value;

            var problems = new Dictionary<string, string[]>();
            var permission = request.Permission?.Trim() ?? "";
            var definition = registry.GetAll().FirstOrDefault(d => string.Equals(d.Permission, permission, StringComparison.OrdinalIgnoreCase));
            if (definition is null)
                problems["permission"] = ["Unknown permission."];
            else if (definition.Sensitivity is not PermissionSensitivity.Critical)
                problems["permission"] = ["Only a Critical permission can have an exception."];

            if (!Enum.TryParse<GrantHolderType>(request.HolderType, ignoreCase: true, out var holderType))
                problems["holderType"] = ["Use Role or User."];
            var holder = request.Holder?.Trim() ?? "";
            if (holder.Length == 0 || holder.Length > 200)
                problems["holder"] = ["Name the role or the user id (up to 200 characters)."];
            else if (holderType == GrantHolderType.User && (!Guid.TryParse(holder, out var holderId) || holderId == requester))
                problems["holder"] = ["Name another user by id; a person cannot hold an exception for themselves."];

            var justification = request.Justification?.Trim() ?? "";
            if (justification.Length < limits.MinJustificationLength || justification.Length > 2000)
                problems["justification"] = [$"Say why it is needed ({limits.MinJustificationLength} to 2000 characters)."];
            var control = string.IsNullOrWhiteSpace(request.CompensatingControl) ? null : request.CompensatingControl.Trim();
            if (control is { Length: > 2000 })
                problems["compensatingControl"] = ["Up to 2000 characters."];
            var owner = request.BusinessOwner?.Trim() ?? "";
            if (owner.Length == 0 || owner.Length > 200)
                problems["businessOwner"] = ["Name the business owner who approved it (up to 200 characters)."];

            if (request.ValidUntil is not { } validUntil || validUntil <= now)
                problems["validUntil"] = ["The end date must be in the future."];
            else if (validUntil - now > limits.MaxDuration)
                problems["validUntil"] = [$"An exception runs at most {limits.MaxDuration.TotalDays.ToString(CultureInfo.InvariantCulture)} days."];
            var reviewDue = request.ReviewDueOn ?? request.ValidUntil;
            if (reviewDue is not { } due || due <= now || (request.ValidUntil is { } end && due > end))
                problems["reviewDueOn"] = ["The review date must be after now and no later than the end date."];

            if (problems.Count > 0)
                return Results.ValidationProblem(problems);

            var saved = await exceptions.CreateAsync(new PermissionException(
                Guid.Empty, definition!.Permission, holderType, holder, justification, control, owner, requester,
                now, request.ValidUntil!.Value, reviewDue!.Value, now), ct);
            await auditWriter.WriteAsync(new AuthorizationAdministrativeChangeEvent(
                "PermissionException", "Recorded", currentUser.UserId?.ToString(), $"exception:{saved.Id}",
                new Dictionary<string, string>
                {
                    ["permission"] = saved.Permission,
                    ["holder"] = $"{saved.HolderType}:{saved.Holder}",
                    ["validUntil"] = saved.ValidUntil.ToString("O", CultureInfo.InvariantCulture),
                }), ct);
            return Results.Created($"exceptions/{saved.Id}", ToView(saved, now));
        });

        group.MapGet("/", async (bool? active, EfPermissionExceptionStore exceptions, TimeProvider clock, CancellationToken ct) =>
        {
            var now = clock.GetUtcNow();
            var list = await exceptions.ListAsync(active == true ? now : null, ct);
            return Results.Ok(list.Select(e => ToView(e, now)));
        });

        group.MapGet("/{id:guid}", async (Guid id, EfPermissionExceptionStore exceptions, TimeProvider clock, CancellationToken ct) =>
            await exceptions.GetAsync(id, ct) is { } found
                ? Results.Ok(ToView(found, clock.GetUtcNow()))
                : Results.NotFound());

        group.MapGet("/findings", async (
            EfPermissionExceptionStore exceptions, IPermissionRegistry registry, TimeProvider clock, CancellationToken ct) =>
        {
            var critical = registry.GetAll().Where(d => d.Sensitivity is PermissionSensitivity.Critical).Select(d => d.Permission);
            var findings = await exceptions.FindingsAsync(critical, clock.GetUtcNow(), ct);
            return Results.Ok(findings.Select(f => new PermissionExceptionFindingView(
                f.Permission, f.HolderType.ToString(), f.Holder, f.HadExpiredException)));
        });

        group.MapPost("/{id:guid}/review", async (
            Guid id, ClaimsPrincipal caller, EfPermissionExceptionStore exceptions, TimeProvider clock,
            IAuthorizationAuditWriter auditWriter, ICurrentUser currentUser, CancellationToken ct) =>
        {
            if (GrantGuards.UserIdOf(caller) is not { } reviewer)
                return Results.Unauthorized();
            if (await exceptions.GetAsync(id, ct) is not { } existing)
                return Results.NotFound();
            if (existing.RequestedBy == reviewer)
                return Results.Problem(detail: "The person who recorded an exception cannot review it.", statusCode: StatusCodes.Status403Forbidden);
            if (!await exceptions.ReviewAsync(id, reviewer, clock.GetUtcNow(), ct))
                return Results.Conflict(new { error = "Not reviewable", message = "It is already reviewed or ended." });

            await auditWriter.WriteAsync(new AuthorizationAdministrativeChangeEvent(
                "PermissionException", "Reviewed", currentUser.UserId?.ToString(), $"exception:{id}", new Dictionary<string, string>()), ct);
            return Results.NoContent();
        });

        group.MapPost("/{id:guid}/revoke", async (
            Guid id, ClaimsPrincipal caller, EfPermissionExceptionStore exceptions, TimeProvider clock,
            IAuthorizationAuditWriter auditWriter, ICurrentUser currentUser, CancellationToken ct) =>
        {
            if (GrantGuards.UserIdOf(caller) is not { } by)
                return Results.Unauthorized();
            if (await exceptions.GetAsync(id, ct) is null)
                return Results.NotFound();
            if (!await exceptions.RevokeAsync(id, by, clock.GetUtcNow(), ct))
                return Results.Conflict(new { error = "Already ended", message = "It has already been ended." });

            await auditWriter.WriteAsync(new AuthorizationAdministrativeChangeEvent(
                "PermissionException", "Revoked", currentUser.UserId?.ToString(), $"exception:{id}", new Dictionary<string, string>()), ct);
            return Results.NoContent();
        });

        return group;
    }

    private static PermissionExceptionView ToView(PermissionException e, DateTimeOffset now) => new(
        e.Id, e.Permission, e.HolderType.ToString(), e.Holder, e.Justification, e.CompensatingControl, e.BusinessOwner, e.RequestedBy,
        e.ValidFrom, e.ValidUntil, e.ReviewDueOn, e.CreatedAt, e.ReviewedBy, e.ReviewedAt, e.RevokedBy, e.RevokedAt, e.IsActiveAt(now));
}
