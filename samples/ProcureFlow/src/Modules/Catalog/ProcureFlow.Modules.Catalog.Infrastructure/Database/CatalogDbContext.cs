using Microsoft.EntityFrameworkCore;
using Modulus.Core.Abstractions;
using Modulus.EntityFrameworkCore;
using Modulus.Events;
using ModulusSample.Modules.Catalog.Domain.Entities;

namespace ModulusSample.Modules.Catalog.Infrastructure.Database;

public sealed class CatalogDbContext(
    DbContextOptions<CatalogDbContext> options,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser,
    DomainEventDispatcher dispatcher,
    IServiceProvider serviceProvider)
    : ModuleDbContext(options, currentTenant, currentUser, dispatcher, serviceProvider)
{
    public const string SchemaName = "catalog";

    public DbSet<Product> Products => Set<Product>();

    protected override string TablePrefix => string.Empty;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema(SchemaName);
    }
}
