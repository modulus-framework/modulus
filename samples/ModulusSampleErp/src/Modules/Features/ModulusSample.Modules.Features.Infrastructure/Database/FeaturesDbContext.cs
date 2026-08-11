using ModulusSample.Modules.Features.Application.Abstractions;
using ModulusSample.Modules.Features.Domain.Entities;
using ModulusSample.Modules.Features.Infrastructure.Configurations;
using Microsoft.EntityFrameworkCore;
using Modulus.Core.Abstractions;
using Modulus.EntityFrameworkCore;
using Modulus.Events;

namespace ModulusSample.Modules.Features.Infrastructure.Database;

public sealed class FeaturesDbContext(
    DbContextOptions<FeaturesDbContext> options,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser,
    DomainEventDispatcher dispatcher,
    IServiceProvider serviceProvider)
    : ModuleDbContext(options, currentTenant, currentUser, dispatcher, serviceProvider), IUnitOfWork
{
    public DbSet<FeatureFlag> FeatureFlags => Set<FeatureFlag>();

    protected override string TablePrefix => string.Empty;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schemas.Features);
        modelBuilder.ApplyConfiguration(new FeatureFlagConfiguration());
        base.OnModelCreating(modelBuilder);
    }
}
