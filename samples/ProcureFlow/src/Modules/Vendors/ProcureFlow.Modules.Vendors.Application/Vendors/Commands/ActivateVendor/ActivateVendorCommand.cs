using ProcureFlow.Shared.Domain;
using ProcureFlow.Modules.Vendors.Application.Vendors.Dtos;
namespace ProcureFlow.Modules.Vendors.Application.Vendors.Commands;

public sealed record ActivateVendorCommand(Guid VendorId) : Modulus.Mediator.Abstractions.ICommand<Result<VendorStatusResponse>>;
