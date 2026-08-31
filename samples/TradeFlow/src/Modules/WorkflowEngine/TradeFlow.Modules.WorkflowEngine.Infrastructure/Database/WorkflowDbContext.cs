using TradeFlow.Modules.WorkflowEngine.Application;
using TradeFlow.Modules.WorkflowEngine.Domain.Constants;
using TradeFlow.Modules.WorkflowEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Modulus.Core.Abstractions;
using Modulus.EntityFrameworkCore;
using Modulus.Events;

namespace TradeFlow.Modules.WorkflowEngine.Infrastructure.Database;

public sealed class WorkflowDbContext(
    DbContextOptions<WorkflowDbContext> options,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser,
    DomainEventDispatcher dispatcher,
    IServiceProvider serviceProvider)
    : ModuleDbContext(options, currentTenant, currentUser, dispatcher, serviceProvider), IUnitOfWork
{
    public DbSet<WorkflowDefinition> WorkflowDefinitions => Set<WorkflowDefinition>();
    public DbSet<WorkflowInstance> WorkflowInstances => Set<WorkflowInstance>();
    public DbSet<WorkflowTask> WorkflowTasks => Set<WorkflowTask>();
    public DbSet<WorkflowEvent> WorkflowEvents => Set<WorkflowEvent>();

    protected override string TablePrefix => string.Empty;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema(Schemas.WorkflowEngine);

        modelBuilder.Entity<WorkflowDefinition>(builder =>
        {
            builder.ToTable("workflow_definitions");
            builder.HasKey(d => d.Id);
            builder.Property(d => d.Key).HasMaxLength(200);
            builder.Property(d => d.Name).HasMaxLength(500);
            builder.Property(d => d.DocumentType).HasMaxLength(200);
            builder.Property(d => d.TriggerEvent).HasMaxLength(200);
            builder.Property(d => d.Status).HasMaxLength(50);
            builder.HasIndex(d => new { d.TenantId, d.Key, d.Version }).IsUnique();
            builder.HasIndex(d => new { d.TenantId, d.Key, d.Status });
        });

        modelBuilder.Entity<WorkflowInstance>(builder =>
        {
            builder.ToTable("workflow_instances");
            builder.HasKey(i => i.Id);
            builder.Property(i => i.DefinitionKey).HasMaxLength(200);
            builder.Property(i => i.DocumentType).HasMaxLength(200);
            builder.Property(i => i.State).HasMaxLength(50);
            builder.Property(i => i.Status).HasMaxLength(50);
            builder.HasIndex(i => new { i.TenantId, i.DocumentType, i.DocumentId });
            builder.HasIndex(i => i.DefinitionId);
        });

        modelBuilder.Entity<WorkflowTask>(builder =>
        {
            builder.ToTable("workflow_tasks");
            builder.HasKey(t => t.Id);
            builder.Property(t => t.StepId).HasMaxLength(200);
            builder.Property(t => t.StepType).HasMaxLength(100);
            builder.Property(t => t.AssigneeRole).HasMaxLength(200);
            builder.Property(t => t.Status).HasMaxLength(50);
            builder.Property(t => t.Decision).HasMaxLength(50);
            builder.HasIndex(t => t.InstanceId);
            builder.HasIndex(t => new { t.AssigneeUserId, t.Status });
        });

        modelBuilder.Entity<WorkflowEvent>(builder =>
        {
            builder.ToTable("workflow_events");
            builder.HasKey(e => e.Id);
            builder.Property(e => e.EventType).HasMaxLength(200);
            builder.Property(e => e.Actor).HasMaxLength(200);
            builder.HasIndex(e => e.InstanceId);
        });
    }
}
