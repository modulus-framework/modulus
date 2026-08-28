using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.SpendAnalysis.Domain.Entities;

/// <summary>
/// Hierarchical category taxonomy (UNSPSC-lite, tenant-extendable).
/// Every PO line should map to a leaf category for spend analytics (BR-SA-01).
/// </summary>
public sealed class CategoryTaxonomy : AggregateRoot
{
    private CategoryTaxonomy() { }

    public CategoryTaxonomy(
        Guid id,
        Guid tenantId,
        string code,
        string name,
        string? description,
        Guid? parentId,
        bool isActive,
        string createdBy)
    {
        Id = id;
        TenantId = tenantId;
        Code = code;
        Name = name;
        Description = description;
        ParentId = parentId;
        IsActive = isActive;
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public Guid? ParentId { get; private set; }
    public bool IsActive { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }
    public string? UpdatedBy { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    public void Update(string name, string? description, Guid? parentId, string updatedBy)
    {
        Name = name;
        Description = description;
        ParentId = parentId;
        UpdatedBy = updatedBy;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void Deactivate(string updatedBy)
    {
        IsActive = false;
        UpdatedBy = updatedBy;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}

/// <summary>
/// Maps a PO line to a category taxonomy node (BR-SA-01).
/// </summary>
public sealed class PoLineCategoryMapping
{
    private PoLineCategoryMapping() { }

    public PoLineCategoryMapping(
        Guid id,
        Guid tenantId,
        Guid poLineId,
        Guid categoryId,
        bool isAutoClassified,
        decimal? confidenceScore,
        string createdBy)
    {
        Id = id;
        TenantId = tenantId;
        PoLineId = poLineId;
        CategoryId = categoryId;
        IsAutoClassified = isAutoClassified;
        ConfidenceScore = confidenceScore;
        CreatedBy = createdBy;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid PoLineId { get; private set; }
    public Guid CategoryId { get; private set; }
    public bool IsAutoClassified { get; private set; }
    public decimal? ConfidenceScore { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }
}

/// <summary>
/// Pre-computed spend aggregation record for fast analytics queries.
/// Refreshed periodically from PO/Invoice data (BR-SA-02).
/// </summary>
public sealed class SpendCubeEntry
{
    private SpendCubeEntry() { }

    public SpendCubeEntry(
        Guid id,
        Guid tenantId,
        DateOnly period,
        Guid? categoryId,
        Guid vendorId,
        Guid? costCenterId,
        string currency,
        decimal poAmount,
        decimal invoicedAmount,
        int poCount,
        int invoiceCount,
        DateTime computedAtUtc)
    {
        Id = id;
        TenantId = tenantId;
        Period = period;
        CategoryId = categoryId;
        VendorId = vendorId;
        CostCenterId = costCenterId;
        Currency = currency;
        PoAmount = poAmount;
        InvoicedAmount = invoicedAmount;
        PoCount = poCount;
        InvoiceCount = invoiceCount;
        ComputedAtUtc = computedAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public DateOnly Period { get; private set; }
    public Guid? CategoryId { get; private set; }
    public Guid VendorId { get; private set; }
    public Guid? CostCenterId { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public decimal PoAmount { get; private set; }
    public decimal InvoicedAmount { get; private set; }
    public int PoCount { get; private set; }
    public int InvoiceCount { get; private set; }
    public DateTime ComputedAtUtc { get; private set; }
}
