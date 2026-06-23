namespace Modulus.EFCore.Integration.Tests;

using Microsoft.EntityFrameworkCore;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Domain;
using Modulus.Core.Abstractions.Entities;
using Modulus.EntityFrameworkCore;
using Modulus.Events;

public sealed class TestDbContext(
    DbContextOptions<TestDbContext> opts,
    ICurrentTenant tenant,
    ICurrentUser   user,
    DomainEventDispatcher dispatcher)
    : ModuleDbContext(opts, tenant, user, dispatcher)
{
    protected override string TablePrefix => "tst_";
    public DbSet<TestProduct> Products => Set<TestProduct>();
}

public class TestProduct
    : AggregateRoot, IAuditableEntity, ISoftDelete, IHasTenantId
{
    public TestProduct() { }
    public TestProduct(Guid id) { Id = id; }
    public string    Name      { get; set; } = default!;
    public decimal   Price     { get; set; }
    // IAuditableEntity
    public DateTime  CreatedAt { get; set; }
    public string?   CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string?   UpdatedBy { get; set; }
    // ISoftDelete
    public bool      IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string?   DeletedBy { get; set; }
    // IHasTenantId
    public Guid      TenantId  { get; set; }
}

// Test stubs
public sealed class TestCurrentTenant : ICurrentTenant
{
    public Guid?   TenantId   { get; set; } = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    public string? TenantSlug { get; set; } = "test-tenant";
    public bool    IsAvailable => TenantId.HasValue;
}
public sealed class TestCurrentUser : ICurrentUser
{
    public Guid?   UserId         => Guid.NewGuid();
    public string? UserName       => "testuser";
    public string? Email          => "test@test.com";
    public bool    IsAuthenticated => true;
    public bool    IsInRole(string r)      => false;
    public bool    HasPermission(string p) => true;
    public IReadOnlyList<string> Permissions => [];
}