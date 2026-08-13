using Modulus.EntityFrameworkCore.Abstractions;
using ModulusSample.Modules.Tenants.Domain.Constants;
using ModulusSample.Modules.Tenants.Domain.Entities;
using ModulusSample.Modules.Tenants.Infrastructure.Configurations;
using Microsoft.EntityFrameworkCore;
using Modulus.Core.Abstractions;
using Modulus.EntityFrameworkCore;
using Modulus.Events;

namespace ModulusSample.Modules.Tenants.Infrastructure.Database;

public sealed class TenantsDbContext(
    DbContextOptions<TenantsDbContext> options,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser,
    DomainEventDispatcher dispatcher,
    IServiceProvider serviceProvider)
    : ModuleDbContext(options, currentTenant, currentUser, dispatcher, serviceProvider), IUnitOfWork
{
    public DbSet<Tenant> Tenants => Set<Tenant>();

    protected override string TablePrefix => string.Empty;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schemas.Tenants);

        modelBuilder.ApplyConfiguration(new TenantConfiguration());

        base.OnModelCreating(modelBuilder);
    }
}
