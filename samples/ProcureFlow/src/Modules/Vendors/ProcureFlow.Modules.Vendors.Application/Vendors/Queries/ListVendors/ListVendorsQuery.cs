using ProcureFlow.Modules.Vendors.Application.Vendors.Dtos;
using ProcureFlow.Modules.Vendors.Domain.Entities;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Vendors.Application.Vendors.Queries;

public sealed record ListVendorsQuery(
    VendorStatus? Status = null,
    string? Country = null,
    VendorType? VendorType = null,
    string? SearchTerm = null) : Modulus.Mediator.Abstractions.IQuery<Result<IReadOnlyList<VendorResponse>>>;
