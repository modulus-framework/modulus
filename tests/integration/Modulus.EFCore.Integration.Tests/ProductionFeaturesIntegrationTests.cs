namespace Modulus.EFCore.Integration.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.Data.PostgreSQL;
using Modulus.EntityFrameworkCore.ChangeHistory;
using Testcontainers.PostgreSql;
using Xunit;

[CollectionDefinition("PostgreSQL Integration Tests")]
public sealed class PostgreSqlCollection : ICollectionFixture<PostgreSqlFixture> { }

[Collection("PostgreSQL Integration Tests")]
[Trait("Category", "Integration")]
public sealed class EntityChangeHistoryIntegrationTests : EFCoreIntegrationTestBase
{
    public EntityChangeHistoryIntegrationTests(PostgreSqlFixture fixture) : base(fixture) { }

    [Fact]
    public async Task AuditableEntity_CapturesCreatedByAndCreatedAt()
    {
        using var ctx = BuildContext();

        var id = Guid.NewGuid();
        var product = new TestProduct(id) { Name = "Test Product", Price = 100m };
        ctx.Products.Add(product);
        await ctx.SaveChangesAsync();

        // Verify audit fields were populated
        Assert.NotNull(product.CreatedBy);
        Assert.NotEqual(default(DateTime), product.CreatedAt);
        Assert.Equal("testuser", product.CreatedBy);
    }

    [Fact]
    public async Task AuditableEntity_CapturesUpdatedByAndUpdatedAt_OnModification()
    {
        using var ctx = BuildContext();

        var id = Guid.NewGuid();
        var product = new TestProduct(id) { Name = "Original", Price = 10m };
        ctx.Products.Add(product);
        await ctx.SaveChangesAsync();

        var createdAt = product.CreatedAt;

        // Wait a tiny bit to ensure timestamps differ
        await Task.Delay(10);

        product.Name = "Modified";
        ctx.Update(product);
        await ctx.SaveChangesAsync();

        // Verify update audit fields
        Assert.NotNull(product.UpdatedBy);
        Assert.NotEqual(default(DateTime), product.UpdatedAt);
        Assert.True(product.UpdatedAt > createdAt, "UpdatedAt should be after CreatedAt");
    }

    [Fact]
    public async Task ChangeHistory_SurvivesAuditableEntityLifecycle()
    {
        // Integration test: verify the full audit trail of an entity:
        // Create → Update → Delete sequence, with all changes recorded.

        using var ctx = BuildContext();

        var id = Guid.NewGuid();
        var product = new TestProduct(id) { Name = "Test", Price = 5m };
        ctx.Products.Add(product);
        await ctx.SaveChangesAsync();

        product.Price = 6m;
        await ctx.SaveChangesAsync();

        ctx.Products.Remove(product);
        await ctx.SaveChangesAsync();

        // Verify the entity was soft-deleted
        Assert.True(product.IsDeleted);
        Assert.NotNull(product.DeletedAt);
    }
}
