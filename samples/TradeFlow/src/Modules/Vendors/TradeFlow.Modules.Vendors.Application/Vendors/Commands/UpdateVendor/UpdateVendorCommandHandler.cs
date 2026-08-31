using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using TradeFlow.Modules.Vendors.Application.Abstractions;
using TradeFlow.Modules.Vendors.Domain.Repositories;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Vendors.Application.Vendors.Commands;

public sealed class UpdateVendorCommandHandler : ICommandHandler<UpdateVendorCommand, Result>
{
    private readonly IVendorRepository _repository;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateVendorCommandHandler(IVendorRepository repository, IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> HandleAsync(UpdateVendorCommand request, CancellationToken ct)
    {
        var vendor = await _repository.GetByIdAsync(request.VendorId, ct);
        if (vendor is null)
            return Result.Failure(Error.NotFound("Vendor.NotFound", "Vendor not found"));

        var result = vendor.Update(
            name: request.Name,
            legalName: request.LegalName,
            country: request.Country,
            vendorType: request.VendorType,
            tin: request.Tin,
            bin: request.Bin,
            email: request.Email,
            phone: request.Phone,
            address: request.Address);

        if (!result.IsSuccess)
            return result;

        await _unitOfWork.CommitAsync(ct);
        return Result.Success();
    }
}
