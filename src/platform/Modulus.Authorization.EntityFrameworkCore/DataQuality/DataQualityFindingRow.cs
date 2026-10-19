namespace Modulus.Authorization.EntityFrameworkCore;

/// <summary>One stored data-quality finding (<c>ModulusDataQualityFindings</c>). Holds attribute names only, never values.</summary>
internal sealed class DataQualityFindingRow
{
    public Guid Id { get; set; }

    /// <summary>The company the decision was made in; <see cref="Guid.Empty"/> when none was in scope.</summary>
    public Guid TenantId { get; set; }

    public DateTime CreatedAt { get; set; }
    public string ResourceType { get; set; } = "";
    public string? RecordId { get; set; }
    public string Action { get; set; } = "";

    /// <summary>The missing attribute names, comma-separated and wrapped in commas (<c>,OrgUnitId,</c>) so one name matches exactly.</summary>
    public string MissingAttributes { get; set; } = "";
}
