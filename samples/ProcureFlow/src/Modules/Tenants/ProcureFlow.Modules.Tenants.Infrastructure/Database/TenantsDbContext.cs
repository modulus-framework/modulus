using ProcureFlow.Modules.Tenants.Application;
using ProcureFlow.Modules.Tenants.Domain.Constants;
using ProcureFlow.Modules.Tenants.Domain.Entities;
using ProcureFlow.Modules.Tenants.Infrastructure.Configurations;
using Microsoft.EntityFrameworkCore;
using Modulus.Core.Abstractions;
using Modulus.EntityFrameworkCore;
using Modulus.Events;

namespace ProcureFlow.Modules.Tenants.Infrastructure.Database;

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

        modelBuilder.ApplyConfiguration(new TenantConfiguration(usePortableJson: Database.IsSqlite()));

        base.OnModelCreating(modelBuilder);
    }
}
