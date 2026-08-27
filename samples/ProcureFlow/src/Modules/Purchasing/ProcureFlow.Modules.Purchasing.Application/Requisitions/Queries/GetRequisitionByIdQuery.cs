using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Purchasing.Application.Requisitions.Dtos;

namespace ModulusSample.Modules.Purchasing.Application.Requisitions.Queries;

public sealed record GetRequisitionByIdQuery(Guid Id) : IQuery<PurchaseRequisitionDto?>;