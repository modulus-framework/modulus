using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Modulus.Authorization.EntityFrameworkCore;
using Modulus.Core.Null;
using Xunit;

namespace Modulus.Authorization.EntityFrameworkCore.Tests;

/// <summary>
/// The scoped-grant unique key must fit the database's index limit. MySQL's is 3072 bytes, and it counts four bytes per utf8mb4
/// character. Other providers have no such limit at this size, so this checks the MySQL model, which needs no server.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ScopedGrantKeyTests
{
    [Fact]
    public void The_mysql_scoped_grant_key_fits_the_3072_byte_index_limit()
    {
        var options = new DbContextOptionsBuilder<AuthorizationStoreDbContext>()
            .UseMySQL("Server=localhost;Database=unused;User=root;Password=unused")
            .Options;
        using var db = new AuthorizationStoreDbContext(options, new NullCurrentTenant());
        var table = db.Model.GetEntityTypes().Single(e => e.GetTableName() == "ModulusScopedGrants");
        var index = table.GetIndexes().Single(i => i.IsUnique);

        // A Guid is char(36) on MySQL, a string is varchar(n), and an enum or integer is four bytes.
        var bytes = 0;
        foreach (var property in index.Properties)
        {
            if (property.Name == "TenantId")
                bytes += 36 * 4;
            else if (property.ClrType == typeof(string))
                bytes += (property.GetMaxLength() ?? throw new InvalidOperationException($"{property.Name} has no length on MySQL.")) * 4;
            else
                bytes += 4;
        }

        bytes.Should().BeLessThanOrEqualTo(3072, "a larger key makes MySQL refuse the table");
    }

    [Fact]
    public void Other_providers_keep_the_longer_scope_column()
    {
        var options = new DbContextOptionsBuilder<AuthorizationStoreDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        using var db = new AuthorizationStoreDbContext(options, new NullCurrentTenant());
        var table = db.Model.GetEntityTypes().Single(e => e.GetTableName() == "ModulusScopedGrants");

        table.FindProperty("Scope")!.GetMaxLength().Should().Be(300);
    }

    [Fact]
    public void Every_index_of_the_authorization_model_fits_the_mysql_key_limit()
    {
        var options = new DbContextOptionsBuilder<AuthorizationStoreDbContext>()
            .UseMySQL("Server=localhost;Database=unused;User=root;Password=unused")
            .Options;
        using var db = new AuthorizationStoreDbContext(options, new NullCurrentTenant());

        var overLimit = new List<string>();
        foreach (var entity in db.Model.GetEntityTypes())
        {
            foreach (var index in entity.GetIndexes())
            {
                // Guid is char(36), a string is varchar(n) (255 when the model names none), an enum or int is four bytes.
                var bytes = index.Properties.Sum(property => property.ClrType switch
                {
                    var t when t == typeof(Guid) => 36 * 4,
                    var t when t == typeof(string) => (property.GetMaxLength() ?? 255) * 4,
                    var t when t == typeof(bool) => 1,
                    var t when t == typeof(long) || t == typeof(DateTimeOffset) || t == typeof(DateTime) => 8,
                    _ => 4,
                });
                if (bytes > 3072)
                    overLimit.Add($"{entity.GetTableName()}({string.Join(", ", index.Properties.Select(p => p.Name))}) = {bytes} bytes");
            }
        }

        overLimit.Should().BeEmpty("MySQL refuses these indexes at startup");
    }
}
