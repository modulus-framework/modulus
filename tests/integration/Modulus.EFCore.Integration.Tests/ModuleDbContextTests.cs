namespace Modulus.EFCore.Integration.Tests;

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

public sealed class ModuleDbContextTests : EFCoreIntegrationTestBase, IClassFixture<PostgreSqlFixture>
{
    public ModuleDbContextTests(PostgreSqlFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Tables_HaveModulePrefix()
    {
        var ctx = BuildContext();
        var tableName = ctx.Model
            .FindEntityType(typeof(TestProduct))!
            .GetTableName();
        tableName.Should().StartWith("tst_");
    }

    [Fact]
    public async Task SaveChangesAsync_SetsAuditFields()
    {
        var ctx = BuildContext();
        var product = new TestProduct(Guid.NewGuid())
        {
            Name = "Widget",
            Price = 9.99m,
            TenantId = Tenant.TenantId!.Value
        };

        ctx.Products.Add(product);
        await ctx.SaveChangesAsync();

        product.CreatedAt.Should().BeCloseTo(DateTime.UtcNow,
            TimeSpan.FromSeconds(5));
        product.CreatedBy.Should().Be("testuser");
    }

    [Fact]
    public async Task SoftDelete_HidesDeletedRecords()
    {
        var ctx = BuildContext();
        var product = new TestProduct(Guid.NewGuid())
        {
            Name = "ToDelete",
            Price = 1m,
            TenantId = Tenant.TenantId!.Value
        };

        ctx.Products.Add(product);
        await ctx.SaveChangesAsync();

        // Delete (soft)
        ctx.Products.Remove(product);
        await ctx.SaveChangesAsync();

        // Should not appear in normal queries
        var found = await ctx.Products
            .FirstOrDefaultAsync(p => p.Id == product.Id);
        found.Should().BeNull();
    }

    [Fact]
    public async Task TenantFilter_IsolatesData()
    {
        var ctx = BuildContext();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        // Insert under tenant A
        Tenant.TenantId = tenantA;

        ctx.Products.Add(new TestProduct(Guid.NewGuid())
        {
            Name = "A Product",
            Price = 1m,
            TenantId = tenantA
        });
        await ctx.SaveChangesAsync();

        // Switch to tenant B — should not see tenant A products
        Tenant.TenantId = tenantB;
        var count = await ctx.Products.CountAsync();
        count.Should().Be(0);
    }
}
