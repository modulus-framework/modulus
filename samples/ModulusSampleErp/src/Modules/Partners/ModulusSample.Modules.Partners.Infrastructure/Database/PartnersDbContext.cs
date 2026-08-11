using Microsoft.EntityFrameworkCore;
using Modulus.Core.Abstractions;
using Modulus.EntityFrameworkCore;
using Modulus.Events;
using ModulusSample.Modules.Partners.Domain.Entities;

namespace ModulusSample.Modules.Partners.Infrastructure.Database;

public sealed class PartnersDbContext(
    DbContextOptions<PartnersDbContext> options,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser,
    DomainEventDispatcher dispatcher,
    IServiceProvider serviceProvider)
    : ModuleDbContext(options, currentTenant, currentUser, dispatcher, serviceProvider)
{
    public const string SchemaName = "partners";

    public DbSet<Partner> Partners => Set<Partner>();

    protected override string TablePrefix => string.Empty;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema(SchemaName);
    }
}
