using ProcureFlow.Modules.Vendors.Application.Vendors.Dtos;
using ProcureFlow.Modules.Vendors.Domain.Entities;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Vendors.Application.Vendors.Commands;

public sealed record CreateVendorCommand(
    string Name,
    string LegalName,
    string Country,
    VendorType VendorType,
    string? Tin,
    string? Bin,
    string? Email,
    string? Phone,
    string? Address) : Modulus.Mediator.Abstractions.ICommand<Result<CreateVendorResponse>>;
