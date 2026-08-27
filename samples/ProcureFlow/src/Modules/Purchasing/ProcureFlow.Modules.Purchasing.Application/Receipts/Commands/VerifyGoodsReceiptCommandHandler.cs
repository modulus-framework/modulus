using Modulus.Mediator.Abstractions;
using Modulus.EntityFrameworkCore.Abstractions;
using ModulusSample.Modules.Purchasing.Domain.Repositories;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Purchasing.Application.Receipts.Commands;

public sealed class VerifyGoodsReceiptCommandHandler(
    IGoodsReceiptRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<VerifyGoodsReceiptCommand, Result>
{
    public async Task<Result> HandleAsync(
        VerifyGoodsReceiptCommand request,
        CancellationToken cancellationToken)
    {
        var receipt = await repository.GetByIdAsync(request.ReceiptId, cancellationToken);

        if (receipt is null)
            return Result.Failure(Error.NotFound("Receipt.NotFound", "Goods receipt not found"));

        var result = receipt.Verify();
        if (result.IsFailure)
            return Result.Failure(result.Error);

        await unitOfWork.CommitAsync(cancellationToken);
        return Result.Success();
    }
}