using ModulusSample.Modules.Identity.Application.Abstractions.Data;
using ModulusSample.Modules.Identity.Domain.Entities;
using ModulusSample.Modules.Identity.Infrastructure.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenIddict.EntityFrameworkCore;

namespace ModulusSample.Modules.Identity.Infrastructure.Database;

public sealed class IdentityDbContext(
    DbContextOptions<IdentityDbContext> options,
    ILogger<IdentityDbContext> logger)
    : DbContext(options), IUnitOfWork
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles { get; set; }
    public DbSet<Permission> Permissions { get; set; }
    public DbSet<DeviceToken> DeviceTokens { get; set; }
    public DbSet<EmailVerificationToken> EmailVerificationTokens { get; set; }
    public DbSet<UserSession> UserSessions { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schemas.Users);

        modelBuilder.ApplyConfiguration(new UserConfiguration());
        modelBuilder.ApplyConfiguration(new RoleConfiguration());
        modelBuilder.ApplyConfiguration(new PermissionConfiguration());
        modelBuilder.ApplyConfiguration(new DeviceTokenConfiguration());
        modelBuilder.ApplyConfiguration(new EmailVerificationTokenConfiguration());
        modelBuilder.ApplyConfiguration(new UserSessionConfiguration());
        modelBuilder.UseOpenIddict();

        base.OnModelCreating(modelBuilder);
    }

    public void ClearChangeTracker()
    {
        ChangeTracker.Clear();
    }
}
