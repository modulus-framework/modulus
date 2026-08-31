using TradeFlow.Modules.Procurement.Domain.Entities;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Procurement.Application.Dtos;

public sealed record ContractResponse(
    Guid Id,
    Guid TenantId,
    string ContractNumber,
    Guid VendorId,
    ContractType Type,
    string Currency,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal CapValue,
    decimal ConsumedValue,
    decimal ConsumedPercent,
    string? Notes,
    ContractStatus Status,
    int RevisionVersion,
    string CreatedBy,
    DateTime CreatedAtUtc);

public sealed record ContractDetailResponse(
    Guid Id,
    Guid TenantId,
    string ContractNumber,
    Guid VendorId,
    ContractType Type,
    string Currency,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal CapValue,
    decimal ConsumedValue,
    decimal ConsumedPercent,
    string? Notes,
    ContractStatus Status,
    int RevisionVersion,
    string CreatedBy,
    DateTime CreatedAtUtc,
    IReadOnlyList<ContractLineResponse> Lines,
    IReadOnlyList<ContractDocumentResponse> Documents,
    IReadOnlyList<ContractMilestoneResponse> Milestones,
    IReadOnlyList<ContractRevisionResponse> Revisions);

public sealed record ContractLineResponse(
    Guid Id,
    Guid? ItemId,
    string? FreeText,
    decimal UnitPrice,
    decimal? MinQuantity,
    string? EscalationJson,
    string Notes);

public sealed record ContractDocumentResponse(
    Guid Id,
    string DocumentType,
    string S3Key,
    DateOnly? ExpiryDate,
    string UploadedBy,
    DateTime UploadedAtUtc);

public sealed record ContractMilestoneResponse(
    Guid Id,
    string Title,
    DateOnly? DueDate,
    string? Deliverables,
    string? SlaJson,
    bool IsCompleted,
    DateTime? CompletedAtUtc);

public sealed record ContractRevisionResponse(
    int Version,
    string Reason,
    string By,
    DateTime AtUtc,
    DateOnly PreviousEndDate,
    DateOnly NewEndDate,
    decimal PreviousCapValue,
    decimal NewCapValue);
