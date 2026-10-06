namespace Modulus.EntityFrameworkCore.Tests;

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions.Entities;
using Xunit;

/// <summary>
/// ModuleDbContext marks TenantId as a concurrency token so every UPDATE/DELETE carries the company in its WHERE clause.
/// That must not look like a schema change: an existing app would otherwise be told to add an empty migration.
/// </summary>
[Trait("Category", "Unit")]
public sealed class TenantConcurrencyTokenMigrationTests
{
    private sealed class Row : IHasTenantId
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
    }

    private sealed class WithToken(DbContextOptions<WithToken> o) : DbContext(o)
    {
        protected override void OnModelCreating(ModelBuilder b)
            => b.Entity<Row>().Property(r => r.TenantId).IsConcurrencyToken();
    }

    private sealed class WithoutToken(DbContextOptions<WithoutToken> o) : DbContext(o)
    {
        protected override void OnModelCreating(ModelBuilder b) => b.Entity<Row>();
    }

    [Fact]
    public void The_token_changes_no_schema()
    {
        using var with = new WithToken(new DbContextOptionsBuilder<WithToken>().UseSqlite("DataSource=:memory:").Options);
        using var without = new WithoutToken(new DbContextOptionsBuilder<WithoutToken>().UseSqlite("DataSource=:memory:").Options);

        var differ = ((IInfrastructure<IServiceProvider>)with).Instance.GetRequiredService<IMigrationsModelDiffer>();
        var differences = differ.GetDifferences(
            without.GetService<IDesignTimeModel>().Model.GetRelationalModel(),
            with.GetService<IDesignTimeModel>().Model.GetRelationalModel());

        differences.Should().BeEmpty();
    }
}
