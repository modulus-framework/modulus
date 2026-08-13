using Modulus.Mediator.Abstractions;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Purchasing.Application.Requisitions.Commands;

public sealed record ApprovePurchaseRequisitionCommand(
    Guid RequisitionId,
    Guid ApproverId) : ICommand<Result>;