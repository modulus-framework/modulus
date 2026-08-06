using ModulusSample.Modules.VirtualFileExplorer.Application.Abstractions;
using ModulusSample.Modules.VirtualFileExplorer.Domain.Entities;
using ModulusSample.Modules.VirtualFileExplorer.Infrastructure.Configurations;
using Microsoft.EntityFrameworkCore;
using Modulus.Core.Abstractions;
using Modulus.EntityFrameworkCore;
using Modulus.Events;

namespace ModulusSample.Modules.VirtualFileExplorer.Infrastructure.Database;

public sealed class VirtualFileExplorerDbContext(
    DbContextOptions<VirtualFileExplorerDbContext> options,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser,
    DomainEventDispatcher dispatcher,
    IServiceProvider serviceProvider)
    : ModuleDbContext(options, currentTenant, currentUser, dispatcher, serviceProvider), IUnitOfWork
{
    public DbSet<VirtualFolder> VirtualFolders => Set<VirtualFolder>();
    public DbSet<VirtualFile> VirtualFiles => Set<VirtualFile>();

    protected override string TablePrefix => string.Empty;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schemas.VirtualFileExplorer);
        modelBuilder.ApplyConfiguration(new VirtualFolderConfiguration());
        modelBuilder.ApplyConfiguration(new VirtualFileConfiguration());
        base.OnModelCreating(modelBuilder);
    }
}