using TradeFlow.Modules.Procurement.Application.Dtos;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Procurement.Application.Queries;

public sealed record GetPrQuery(Guid PrId) : Modulus.Mediator.Abstractions.IQuery<Result<PurchaseRequisitionResponse>>;

public sealed record GetRfqQuery(Guid RfqId) : Modulus.Mediator.Abstractions.IQuery<Result<RfqResponse>>;

public sealed record GetPoQuery(Guid PoId) : Modulus.Mediator.Abstractions.IQuery<Result<PurchaseOrderResponse>>;

public sealed record ListPrsQuery(int? FiscalYear, Guid? CostCenterId) : Modulus.Mediator.Abstractions.IQuery<Result<IReadOnlyList<PurchaseRequisitionResponse>>>;

public sealed record ListPosQuery() : Modulus.Mediator.Abstractions.IQuery<Result<IReadOnlyList<PurchaseOrderResponse>>>;