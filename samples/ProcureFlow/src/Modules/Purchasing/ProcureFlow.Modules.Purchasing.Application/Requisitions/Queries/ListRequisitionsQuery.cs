using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Purchasing.Application.Requisitions.Dtos;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Purchasing.Application.Requisitions.Queries;

public sealed record ListRequisitionsQuery(int Page, int PageSize) : IQuery<PagedResult<PurchaseRequisitionDto>>;