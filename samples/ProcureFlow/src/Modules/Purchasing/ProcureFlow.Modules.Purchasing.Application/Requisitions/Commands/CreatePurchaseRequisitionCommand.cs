using Modulus.Mediator.Abstractions;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Purchasing.Application.Requisitions.Commands;

public sealed record CreatePurchaseRequisitionCommand(
    string RequisitionNumber,
    Guid OrgUnitId) : ICommand<Result<Guid>>;