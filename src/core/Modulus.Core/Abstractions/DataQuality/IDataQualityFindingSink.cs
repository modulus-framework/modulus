namespace Modulus.Core.Abstractions.DataQuality;

/// <summary>
/// Receives a data-quality finding each time an access decision was refused because a record lacks an attribute the policy
/// needs (<c>MISSING_ATTRIBUTE</c>). The finding names the attributes only, never their values, so it can be stored and
/// reviewed without leaking the record. An implementation must not throw: the decision is already a denial, and a failing
/// sink must not turn it into an error response.
/// </summary>
public interface IDataQualityFindingSink
{
    /// <summary>Records one finding.</summary>
    void Record(DataQualityFinding finding);
}

/// <summary>
/// A record that cannot be authorized because an attribute the policy depends on is missing from it.
/// </summary>
/// <param name="TenantId">The tenant the decision was made in, or <see langword="null"/> when none was in scope.</param>
/// <param name="ResourceType">The resource type's name.</param>
/// <param name="RecordId">The record's id as text, when the resource exposes an <c>Id</c>; otherwise <see langword="null"/>.</param>
/// <param name="Action">The action that was refused.</param>
/// <param name="MissingAttributes">The attribute names that were absent (for example <c>OrgUnitId</c>), never their values.</param>
public sealed record DataQualityFinding(
    Guid? TenantId,
    string ResourceType,
    string? RecordId,
    string Action,
    IReadOnlyList<string> MissingAttributes);
