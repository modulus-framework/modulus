using Modulus.Mediator.Abstractions;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Purchasing.Application.Requisitions.Commands;

public sealed record AddRequisitionLineCommand(
    Guid RequisitionId,
    Guid SupplierId,
    string Description,
    decimal Quantity,
    decimal UnitPrice) : ICommand<Result>;