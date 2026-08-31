using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Customs.Domain.Entities;

public enum HsMappingStatus
{
    Draft = 1,
    PendingApproval = 2,
    Approved = 3,
    Rejected = 4,
    Overridden = 5,
}

/// <summary>
/// Item-to-HS code mapping (BR-HS-02..03). Links items to their 8-digit BD
/// tariff classification with confidence level and maker-checker approval.
/// Per-consignment overrides allowed (logged; variance tracked).
/// </summary>
public sealed class ItemHsMapping : AggregateRoot
{
    private ItemHsMapping() { }

    private ItemHsMapping(Guid id, Guid tenantId, Guid itemId, string hsCode, decimal confidence,
        string? notes)
    {
        Id = id;
        TenantId = tenantId;
        ItemId = itemId;
        HsCode = hsCode;
        Confidence = confidence;
        Notes = notes;
        Status = HsMappingStatus.Draft;
    }

    public Guid TenantId { get; private set; }
    public Guid ItemId { get; private set; }
    public string HsCode { get; private set; } = null!;
    public decimal Confidence { get; private set; }
    public HsMappingStatus Status { get; private set; }
    public string? Notes { get; private set; }
    public Guid? MappedBy { get; private set; }
    public DateTime? MappedAtUtc { get; private set; }
    public Guid? ApprovedBy { get; private set; }
    public DateTime? ApprovedAtUtc { get; private set; }
    public string? RejectionReason { get; private set; }
    public bool IsConsignmentOverride { get; private set; }
    public Guid? OverrideFileId { get; private set; }

    public static ItemHsMapping Create(Guid tenantId, Guid itemId, string hsCode,
        decimal confidence, string? notes, bool isConsignmentOverride = false, Guid? fileId = null)
    {
        if (string.IsNullOrWhiteSpace(hsCode) || hsCode.Length is < 4 or > 12)
            throw new ArgumentException("HS code must be 4–12 digits", nameof(hsCode));
        if (confidence is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(confidence), "Confidence must be 0..1");

        var mapping = new ItemHsMapping(Guid.NewGuid(), tenantId, itemId, hsCode.Trim(), confidence, notes);
        mapping.MappedAtUtc = DateTime.UtcNow;
        mapping.IsConsignmentOverride = isConsignmentOverride;
        mapping.OverrideFileId = fileId;
        return mapping;
    }

    public void UpdateHsCode(string hsCode, decimal confidence, string? notes)
    {
        if (Status is HsMappingStatus.Approved)
            throw new InvalidOperationException("Cannot change an approved mapping — create a new one");
        if (string.IsNullOrWhiteSpace(hsCode) || hsCode.Length is < 4 or > 12)
            throw new ArgumentException("HS code must be 4–12 digits", nameof(hsCode));
        if (confidence is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(confidence), "Confidence must be 0..1");

        HsCode = hsCode.Trim();
        Confidence = confidence;
        Notes = notes;
        MappedAtUtc = DateTime.UtcNow;
    }

    public Result Submit()
    {
        if (Status is not (HsMappingStatus.Draft or HsMappingStatus.Rejected))
            return Result.Failure(Error.BusinessRule("HsMapping.Status", "Only draft/rejected mappings can be submitted"));
        Status = HsMappingStatus.PendingApproval;
        return Result.Success();
    }

    public Result Approve(Guid approvedBy)
    {
        if (Status != HsMappingStatus.PendingApproval)
            return Result.Failure(Error.BusinessRule("HsMapping.Status", "Only pending mappings can be approved"));
        Status = HsMappingStatus.Approved;
        ApprovedBy = approvedBy;
        ApprovedAtUtc = DateTime.UtcNow;
        return Result.Success();
    }

    public Result Reject(string reason)
    {
        if (Status != HsMappingStatus.PendingApproval)
            return Result.Failure(Error.BusinessRule("HsMapping.Status", "Only pending mappings can be rejected"));
        Status = HsMappingStatus.Rejected;
        RejectionReason = reason;
        return Result.Success();
    }
}
