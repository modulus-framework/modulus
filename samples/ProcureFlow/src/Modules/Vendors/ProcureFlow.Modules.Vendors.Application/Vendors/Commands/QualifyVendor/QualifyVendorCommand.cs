using ProcureFlow.Shared.Domain;
using ProcureFlow.Modules.Vendors.Application.Vendors.Dtos;
namespace ProcureFlow.Modules.Vendors.Application.Vendors.Commands;

public sealed record QualifyVendorCommand(
    Guid VendorId,
    string Category,
    string CertificateNumber,
    DateOnly ValidFrom,
    DateOnly ValidTo) : Modulus.Mediator.Abstractions.ICommand<Result<VendorStatusResponse>>;
