using Modulus.Mediator.Abstractions;
using Modulus.EntityFrameworkCore.Abstractions;
using modulus.Modules.Catalog.Domain;

namespace modulus.Modules.Catalog.Application;

public sealed class CreateProductHandler(
    IProductRepository repo,
    IUnitOfWork unitOfWork)
    : ICommandHandler<CreateProductCommand, Guid>
{
    public async Task<Guid> HandleAsync(
        CreateProductCommand command,
        CancellationToken ct)
    {
        var entity = new Product { Name = command.Name };
        await repo.AddAsync(entity, ct);
        await unitOfWork.CommitAsync(ct);
        return entity.Id;
    }
}
