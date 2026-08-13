using Modulus.Mediator.Abstractions;
using Modulus.EntityFrameworkCore.Abstractions;
using ModulusSample.Modules.Purchasing.Domain.Repositories;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Purchasing.Application.Receipts.Commands;

public sealed class AddReceiptLineCommandHandler(
    IGoodsReceiptRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<AddReceiptLineCommand, Result>
{
    public async Task<Result> HandleAsync(
        AddReceiptLineCommand request,
        CancellationToken cancellationToken)
    {
        var receipt = await repository.GetByIdAsync(request.ReceiptId, cancellationToken);

        if (receipt is null)
            return Result.Failure(Error.NotFound("Receipt.NotFound", "Goods receipt not found"));

        var result = receipt.AddLine(request.ProductId, request.QuantityReceived, request.LotNumber);
        if (result.IsFailure)
            return Result.Failure(result.Error);

        await unitOfWork.CommitAsync(cancellationToken);
        return Result.Success();
    }
}