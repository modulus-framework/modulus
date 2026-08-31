using TradeFlow.Modules.OrgStructure.Application.Abstractions;
using TradeFlow.Modules.OrgStructure.Domain.Constants;
using TradeFlow.Modules.OrgStructure.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Modulus.Core.Abstractions;
using Modulus.EntityFrameworkCore;
using Modulus.Events;

namespace TradeFlow.Modules.OrgStructure.Infrastructure.Database;

public sealed class OrgStructureDbContext(
    DbContextOptions<OrgStructureDbContext> options,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser,
    DomainEventDispatcher dispatcher,
    IServiceProvider serviceProvider)
    : ModuleDbContext(options, currentTenant, currentUser, dispatcher, serviceProvider), IUnitOfWork
{
    public DbSet<OrgNode> OrgNodes => Set<OrgNode>();
    public DbSet<Position> Positions => Set<Position>();

    protected override string TablePrefix => string.Empty;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema(Schemas.OrgStructure);

        modelBuilder.Entity<OrgNode>(builder =>
        {
            builder.ToTable("org_nodes");
            builder.HasKey(n => n.Id);

            builder.Property(n => n.LtreePath)
                .HasColumnType("ltree");

            builder.HasIndex(n => n.LtreePath)
                .HasMethod("gist");

            builder.HasIndex(n => new { n.TenantId, n.Code })
                .IsUnique();

            builder.HasIndex(n => new { n.TenantId, n.ParentId });

            builder.HasIndex(n => new { n.TenantId, n.NodeType });
        });

        modelBuilder.Entity<Position>(builder =>
        {
            builder.ToTable("positions");
            builder.HasKey(p => p.Id);

            builder.HasIndex(p => new { p.TenantId, p.OrgNodeId, p.Code })
                .IsUnique();

            builder.HasIndex(p => new { p.TenantId, p.OrgNodeId });

            builder.OwnsMany(p => p.Assignments, a =>
            {
                a.WithOwner().HasForeignKey("PositionId");
                a.HasKey("PositionId", nameof(PositionAssignment.Id));
                a.ToTable("position_assignments");

                a.HasIndex("UserId");
                a.HasIndex("IsActive", "EffectiveFrom");
            });
        });
    }
}
