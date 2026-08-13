namespace ModulusSample.Modules.Purchasing.Application.Requisitions.Dtos;

public sealed record RequisitionLineDto(
    Guid Id,
    Guid SupplierId,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal LineTotal);