using System.Text.Json.Serialization;
using Modulus.Core.Abstractions;

namespace Modulus.MultiTenancy.EntityFrameworkCore;

/// <summary>A company as the administration screens show it: its state, its trial end, its seats and how many are in use.</summary>
/// <param name="Id">The company.</param>
/// <param name="Slug">The company's short name, used in the <c>X-Tenant-Id</c> header.</param>
/// <param name="DisplayName">The name shown to people.</param>
/// <param name="GroupId">The group of companies the company belongs to, if any.</param>
/// <param name="Status">Trial, Active, Suspended or Closed.</param>
/// <param name="TrialEndsAt">When a trial ends, for a company on trial.</param>
/// <param name="MaxUsers">The seat limit, or null when there is none.</param>
/// <param name="ActiveMembers">Members whose membership is active now.</param>
/// <param name="CreatedAtUtc">When the company was created.</param>
public sealed record TenantAdminView(
    Guid Id,
    string Slug,
    string? DisplayName,
    Guid? GroupId,
    [property: JsonConverter(typeof(JsonStringEnumConverter<TenantStatus>))] TenantStatus Status,
    DateTimeOffset? TrialEndsAt,
    int? MaxUsers,
    int ActiveMembers,
    DateTimeOffset CreatedAtUtc);

/// <summary>One membership of a company.</summary>
/// <param name="UserId">The member.</param>
/// <param name="IsActive">False when the membership was revoked (the row is kept).</param>
/// <param name="AddedAtUtc">When the membership was created.</param>
public sealed record TenantMemberView(Guid UserId, bool IsActive, DateTimeOffset AddedAtUtc);
