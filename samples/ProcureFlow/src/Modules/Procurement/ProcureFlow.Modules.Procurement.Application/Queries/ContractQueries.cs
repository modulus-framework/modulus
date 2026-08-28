using ProcureFlow.Modules.Procurement.Application.Dtos;
using ProcureFlow.Modules.Procurement.Domain.Entities;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Procurement.Application.Queries;

public sealed record GetContractByIdQuery(
    Guid ContractId) : Modulus.Mediator.Abstractions.IQuery<Result<ContractDetailResponse>>;

public sealed record ListContractsQuery(
    ContractStatus? Status = null,
    Guid? VendorId = null) : Modulus.Mediator.Abstractions.IQuery<Result<IReadOnlyList<ContractResponse>>>;

public sealed record GetExpiringContractsQuery(
    int WithinDays = 60) : Modulus.Mediator.Abstractions.IQuery<Result<IReadOnlyList<ContractResponse>>>;

public sealed record CheckMaverickPurchaseQuery(
    Guid VendorId,
    Guid ItemId) : Modulus.Mediator.Abstractions.IQuery<Result<MaverickCheckResponse>>;

public sealed record MaverickCheckResponse(
    bool IsMaverick,
    Guid? ActiveContractId,
    string? ContractNumber,
    decimal? ContractUnitPrice);
