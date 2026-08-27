using Modulus.Mediator.Abstractions;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Purchasing.Application.Requisitions.Commands;

public sealed record RejectPurchaseRequisitionCommand(
    Guid RequisitionId,
    string Reason) : ICommand<Result>;