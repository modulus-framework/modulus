using TradeFlow.Modules.Configuration.Application;
using TradeFlow.Modules.Configuration.Domain.Entities;
using TradeFlow.Modules.Configuration.Infrastructure.Configurations;
using Microsoft.EntityFrameworkCore;
using Modulus.Core.Abstractions;
using Modulus.EntityFrameworkCore;
using Modulus.Events;

namespace TradeFlow.Modules.Configuration.Infrastructure.Database;

public sealed class ConfigurationDbContext(
    DbContextOptions<ConfigurationDbContext> options,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser,
    DomainEventDispatcher dispatcher,
    IServiceProvider serviceProvider)
    : ModuleDbContext(options, currentTenant, currentUser, dispatcher, serviceProvider), IUnitOfWork
{
    public DbSet<Setting> Settings => Set<Setting>();
    public DbSet<FeatureFlag> FeatureFlags => Set<FeatureFlag>();

    protected override string TablePrefix => string.Empty;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schemas.Configuration);
        modelBuilder.ApplyConfiguration(new SettingConfiguration());
        modelBuilder.ApplyConfiguration(new FeatureFlagConfiguration());
        base.OnModelCreating(modelBuilder);
    }
}