using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Modulus.Authorization.Resources;
using Modulus.Authorization.Sharing;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Exceptions;

namespace Modulus.Authorization.Management;

public static partial class AuthorizationManagementExtensions
{
    /// <summary>A share to make: the grantee and how many hours it runs (up to the configured limit).</summary>
    public sealed record RecordShareRequest(Guid GranteeUserId, double Hours);

    /// <summary>A share as the record's sharers and grantees see it.</summary>
    public sealed record RecordShareResponse(
        Guid Id, Guid GranteeUserId, Guid SharedByUserId, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, DateTimeOffset? RevokedAt, bool Active);

    // The longest share a request may name, far above any sensible limit, so a bad number is refused before a TimeSpan overflow.
    private const double MaxRequestHours = 24 * 3650;

    // Shares of a record (A8-1). Only a person who can read the record on their own rights may create or list its shares: a grantee
    // reached through a share is refused, so a share cannot be passed on. Revoking is the sharer's alone.
    private static void MapRecordShares(RouteGroupBuilder group)
    {
        group.MapPost("/{resourceType}/{id}/shares", async (
            string resourceType, string id, RecordShareRequest body,
            IResourceLocator locator, IResourceAuthorizer authorizer, RecordShares shares, ICurrentUser user, CancellationToken ct) =>
        {
            if (user.UserId is not { } sharedBy)
                return Results.Unauthorized();
            if (!double.IsFinite(body.Hours) || body.Hours <= 0 || body.Hours > MaxRequestHours)
                return Results.BadRequest(new { error = "hours_out_of_range" });

            var record = await locator.FindAsync(resourceType, id, ct);
            if (record is null)
                return Results.NotFound();
            if (!await CanShareAsync(authorizer, shares, record, ct))
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            if (!RecordShares.TryKeyOf(record, out var type, out var key))
                return Results.BadRequest(new { error = "record_has_no_id" });

            try
            {
                var share = await shares.CreateAsync(type, key, body.GranteeUserId, sharedBy, TimeSpan.FromHours(body.Hours), ct);
                return Results.Json(ToResponse(share, DateTimeOffset.UtcNow), statusCode: StatusCodes.Status201Created);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = "invalid_share", detail = ex.Message });
            }
        });

        group.MapGet("/{resourceType}/{id}/shares", async (
            string resourceType, string id,
            IResourceLocator locator, IResourceAuthorizer authorizer, RecordShares shares, CancellationToken ct) =>
        {
            var record = await locator.FindAsync(resourceType, id, ct);
            if (record is null)
                return Results.NotFound();
            if (!await CanShareAsync(authorizer, shares, record, ct))
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            if (!RecordShares.TryKeyOf(record, out var type, out var key))
                return Results.BadRequest(new { error = "record_has_no_id" });

            var now = DateTimeOffset.UtcNow;
            var list = await shares.ForRecordAsync(type, key, ct);
            return Results.Ok(list.Select(s => ToResponse(s, now)));
        });

        group.MapDelete("/shares/{shareId:guid}", async (
            Guid shareId, RecordShares shares, ICurrentUser user, CancellationToken ct) =>
        {
            if (user.UserId is not { } revokedBy)
                return Results.Unauthorized();
            try
            {
                return await shares.RevokeAsync(shareId, revokedBy, ct) ? Results.NoContent() : Results.NotFound();
            }
            catch (ForbiddenException)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }
        });
    }

    // The caller may share or list shares only on their own read of the record: a decision made through a share does not count.
    private static async Task<bool> CanShareAsync(IResourceAuthorizer authorizer, RecordShares shares, object record, CancellationToken ct)
    {
        var decision = await authorizer.AuthorizeAsync(record, shares.SharedAction, ct);
        return decision.IsAllowed && decision.Code != AccessReasonCodes.Shared;
    }

    private static RecordShareResponse ToResponse(RecordShare share, DateTimeOffset now)
        => new(share.Id, share.GranteeUserId, share.SharedByUserId, share.CreatedAt, share.ExpiresAt, share.RevokedAt, share.IsActiveAt(now));
}
