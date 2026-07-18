using Modulus.Mediator.Abstractions;
using Modulus.EntityFrameworkCore.Abstractions;
using Modulus.Core.Abstractions.Common;
using modulus.Modules.Catalog.Domain;

namespace modulus.Modules.Catalog.Application;

public sealed class DeleteProductHandler(
    IProductRepository repo,
    IUnitOfWork unitOfWork)
    : ICommandHandler<DeleteProductCommand, Unit>
{
    public async Task<Unit> HandleAsync(
        DeleteProductCommand command,
        CancellationToken ct)
    {
        var entity = await repo.GetByIdAsync(command.Id, ct)
            ?? throw new InvalidOperationException(
                "Product not found: " + command.Id);

        await repo.DeleteAsync(entity, ct);
        await unitOfWork.CommitAsync(ct);

        return Unit.Value;
    }
}
