using Modulus.Mediator.Abstractions;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Purchasing.Application.Orders.Commands;

public sealed record CreatePurchaseOrderCommand(
    string OrderNumber,
    Guid RequisitionId,
    Guid SupplierId,
    Guid OrgUnitId) : ICommand<Result<Guid>>;