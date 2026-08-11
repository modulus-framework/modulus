using Microsoft.EntityFrameworkCore;
using Modulus.Core.Abstractions;
using Modulus.Events;
using Modulus.EntityFrameworkCore;
using ModulusSample.Modules.Media.Application;
using ModulusSample.Modules.Media.Domain.Entities;
using ModulusSample.Modules.Media.Infrastructure.Configurations;

namespace ModulusSample.Modules.Media.Infrastructure.Database;

/// <summary>
/// The Media module's own DbContext. Each module owns its context
/// (with its own tables/connection) so modules are independently deployable.
/// Implements the module's <see cref="IUnitOfWork"/> so handlers can save
/// without depending on EF Core.
/// </summary>
public sealed class MediaDbContext(
    DbContextOptions<MediaDbContext> options,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser,
    DomainEventDispatcher dispatcher,
    IServiceProvider sp)
    : ModuleDbContext(options, currentTenant, currentUser, dispatcher, sp), IUnitOfWork
{
    protected override string TablePrefix => "media_";

    public DbSet<MediaFile> MediaFiles => Set<MediaFile>();
    public DbSet<MediaFolder> MediaFolders => Set<MediaFolder>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schemas.Media);

        modelBuilder.ApplyConfiguration(new MediaFileConfiguration());
        modelBuilder.ApplyConfiguration(new MediaFolderConfiguration());

        base.OnModelCreating(modelBuilder);
    }
}
