using Modulus.Mediator.Abstractions;
using Modulus.EntityFrameworkCore.Abstractions;
using ModulusSample.Modules.Purchasing.Domain.Repositories;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Purchasing.Application.Orders.Commands;

public sealed class SendPurchaseOrderCommandHandler(
    IPurchaseOrderRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<SendPurchaseOrderCommand, Result>
{
    public async Task<Result> HandleAsync(
        SendPurchaseOrderCommand request,
        CancellationToken cancellationToken)
    {
        var order = await repository.GetByIdAsync(request.OrderId, cancellationToken);

        if (order is null)
            return Result.Failure(Error.NotFound("Order.NotFound", "Purchase order not found"));

        var result = order.Send();
        if (result.IsFailure)
            return Result.Failure(result.Error);

        await unitOfWork.CommitAsync(cancellationToken);
        return Result.Success();
    }
}