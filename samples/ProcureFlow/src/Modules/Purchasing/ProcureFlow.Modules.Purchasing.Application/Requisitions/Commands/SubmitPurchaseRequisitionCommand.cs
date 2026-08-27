using Modulus.Mediator.Abstractions;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Purchasing.Application.Requisitions.Commands;

public sealed record SubmitPurchaseRequisitionCommand(
    Guid RequisitionId) : ICommand<Result>;