using Modulus.EntityFrameworkCore.Abstractions;
using ModulusSample.Modules.Settings.Domain.Entities;
using ModulusSample.Modules.Settings.Infrastructure.Configurations;
using Microsoft.EntityFrameworkCore;
using Modulus.Core.Abstractions;
using Modulus.EntityFrameworkCore;
using Modulus.Events;

namespace ModulusSample.Modules.Settings.Infrastructure.Database;

public sealed class SettingsDbContext(
    DbContextOptions<SettingsDbContext> options,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser,
    DomainEventDispatcher dispatcher,
    IServiceProvider serviceProvider)
    : ModuleDbContext(options, currentTenant, currentUser, dispatcher, serviceProvider), IUnitOfWork
{
    public DbSet<Setting> Settings => Set<Setting>();

    protected override string TablePrefix => string.Empty;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schemas.Settings);
        modelBuilder.ApplyConfiguration(new SettingConfiguration());
        base.OnModelCreating(modelBuilder);
    }
}
