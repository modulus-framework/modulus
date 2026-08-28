using ProcureFlow.Modules.Vendors.Application.Vendors.Dtos;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Vendors.Application.Vendors.Queries;

public sealed record GetVendorByIdQuery(Guid VendorId) : Modulus.Mediator.Abstractions.IQuery<Result<VendorDetailResponse>>;
