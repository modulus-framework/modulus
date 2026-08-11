using Microsoft.EntityFrameworkCore;
using Modulus.Core.Abstractions;
using Modulus.EntityFrameworkCore;
using Modulus.Events;
using ModulusSample.Modules.Sales.Domain.Entities;

namespace ModulusSample.Modules.Sales.Infrastructure.Database;

public sealed class SalesDbContext(
    DbContextOptions<SalesDbContext> options,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser,
    DomainEventDispatcher dispatcher,
    IServiceProvider serviceProvider)
    : ModuleDbContext(options, currentTenant, currentUser, dispatcher, serviceProvider)
{
    public const string SchemaName = "sales";

    public DbSet<SalesOrder> Orders => Set<SalesOrder>();

    protected override string TablePrefix => string.Empty;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema(SchemaName);

        modelBuilder.Entity<SalesOrder>()
            .OwnsMany(o => o.Lines, lb =>
            {
                lb.WithOwner().HasForeignKey("SalesOrderId");
                lb.HasKey(nameof(OrderLine.Id));
                lb.ToTable("OrderLines", SchemaName);
            });
    }
}
