using TradeFlow.Modules.Notifications.Application;
using TradeFlow.Modules.Notifications.Domain.Entities;
using TradeFlow.Modules.Notifications.Infrastructure.Configurations;
using Microsoft.EntityFrameworkCore;
using Modulus.Core.Abstractions;
using Modulus.EntityFrameworkCore;
using Modulus.Events;

namespace TradeFlow.Modules.Notifications.Infrastructure.Database;

public sealed class NotificationsDbContext(
    DbContextOptions<NotificationsDbContext> options,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser,
    DomainEventDispatcher dispatcher,
    IServiceProvider serviceProvider)
    : ModuleDbContext(options, currentTenant, currentUser, dispatcher, serviceProvider), IUnitOfWork
{
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<NotificationRule> NotificationRules => Set<NotificationRule>();
    public DbSet<NotificationTemplate> NotificationTemplates => Set<NotificationTemplate>();
    public DbSet<NotificationPreference> NotificationPreferences => Set<NotificationPreference>();
    public DbSet<NotificationLog> NotificationLogs => Set<NotificationLog>();

    protected override string TablePrefix => string.Empty;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schemas.Notifications);
        modelBuilder.ApplyConfiguration(new NotificationConfiguration());
        modelBuilder.ApplyConfiguration(new NotificationRuleConfiguration());
        modelBuilder.ApplyConfiguration(new NotificationTemplateConfiguration());
        modelBuilder.ApplyConfiguration(new NotificationPreferenceConfiguration());
        modelBuilder.ApplyConfiguration(new NotificationLogConfiguration());
        base.OnModelCreating(modelBuilder);
    }
}
