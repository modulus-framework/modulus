using ModulusSample.Modules.Notifications.Application.Abstractions;
using ModulusSample.Modules.Notifications.Domain.Entities;
using ModulusSample.Modules.Notifications.Infrastructure.Configurations;
using Microsoft.EntityFrameworkCore;
using Modulus.Core.Abstractions;
using Modulus.EntityFrameworkCore;
using Modulus.Events;

namespace ModulusSample.Modules.Notifications.Infrastructure.Database;

public sealed class NotificationsDbContext(
    DbContextOptions<NotificationsDbContext> options,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser,
    DomainEventDispatcher dispatcher,
    IServiceProvider serviceProvider)
    : ModuleDbContext(options, currentTenant, currentUser, dispatcher, serviceProvider), IUnitOfWork
{
    public DbSet<Notification> Notifications => Set<Notification>();

    protected override string TablePrefix => string.Empty;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schemas.Notifications);
        modelBuilder.ApplyConfiguration(new NotificationConfiguration());
        base.OnModelCreating(modelBuilder);
    }
}