namespace Modulus.Authorization.EntityFrameworkCore;

/// <summary>
/// The filters of a finding read. Every filter is optional; the page is <see cref="Skip"/> and <see cref="Take"/>, newest first.
/// </summary>
/// <param name="TenantId">The company to read; <see cref="Guid.Empty"/> for findings made with no company; <c>null</c> for every company.</param>
/// <param name="ResourceType">The resource type's name, matched exactly.</param>
/// <param name="Attribute">Only findings whose missing attributes include this name, matched exactly.</param>
/// <param name="Skip">How many findings to skip.</param>
/// <param name="Take">The page size, clamped to <see cref="MaxTake"/>.</param>
public sealed record DataQualityFindingQuery(
    Guid? TenantId = null,
    string? ResourceType = null,
    string? Attribute = null,
    int Skip = 0,
    int Take = DataQualityFindingQuery.DefaultTake)
{
    /// <summary>The page size when none is given.</summary>
    public const int DefaultTake = 100;

    /// <summary>The largest page a read returns.</summary>
    public const int MaxTake = 500;
}

/// <summary>One stored data-quality finding. Holds attribute names only, never values.</summary>
/// <param name="Id">The finding's id.</param>
/// <param name="TenantId">The company the decision was made in; <see cref="Guid.Empty"/> when none was in scope.</param>
/// <param name="CreatedAt">When the finding was recorded, in UTC.</param>
/// <param name="ResourceType">The resource type's name.</param>
/// <param name="RecordId">The record's id as text, when the resource exposes one.</param>
/// <param name="Action">The action that was refused.</param>
/// <param name="MissingAttributes">The attribute names that were absent.</param>
public sealed record DataQualityFindingRecord(
    Guid Id,
    Guid TenantId,
    DateTime CreatedAt,
    string ResourceType,
    string? RecordId,
    string Action,
    IReadOnlyList<string> MissingAttributes);
