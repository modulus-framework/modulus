namespace ModulusSample.Modules.Purchasing.Application.Requisitions.Dtos;

public sealed record PurchaseRequisitionDto(
    Guid Id,
    string RequisitionNumber,
    Guid RequesterId,
    Guid? ApproverId,
    decimal TotalAmount,
    string Status,
    Guid OrgUnitId,
    Guid TenantId);