using Modulus.Data.Abstractions;

namespace modulus.Modules.Catalog.Domain;

public interface IProductRepository : IRepository<Product>
{
    Task<IReadOnlyList<Product>> GetAllAsync(CancellationToken ct);
}
