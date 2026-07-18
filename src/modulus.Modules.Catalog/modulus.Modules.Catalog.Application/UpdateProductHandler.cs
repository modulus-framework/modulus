using Modulus.Mediator.Abstractions;
using Modulus.EntityFrameworkCore.Abstractions;
using modulus.Modules.Catalog.Domain;

namespace modulus.Modules.Catalog.Application;

public sealed class UpdateProductHandler(
    IProductRepository repo,
    IUnitOfWork unitOfWork)
    : ICommandHandler<UpdateProductCommand, UpdateProductResult>
{
    public async Task<UpdateProductResult> HandleAsync(
        UpdateProductCommand command,
        CancellationToken ct)
    {
        var entity = await repo.GetByIdAsync(command.Id, ct)
            ?? throw new InvalidOperationException(
                "Product not found: " + command.Id);

        entity.Name = command.Name;

        await repo.UpdateAsync(entity, ct);
        await unitOfWork.CommitAsync(ct);

        return new UpdateProductResult(entity.Id);
    }
}
