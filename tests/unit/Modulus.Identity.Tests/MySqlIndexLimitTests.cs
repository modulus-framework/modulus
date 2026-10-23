using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Modulus.Core.Null;
using Modulus.Identity.EntityFrameworkCore;
using Xunit;

namespace Modulus.Identity.Tests;

/// <summary>
/// MySQL refuses an index whose key is longer than 3072 bytes, and it counts four bytes per utf8mb4 character. A generated MySQL API
/// fails at startup on such an index, and the test database (SQLite) never shows it. These tests check the MySQL models of the identity
/// context, which need no server.
/// </summary>
[Trait("Category", "Unit")]
public sealed class MySqlIndexLimitTests
{
    private const int LimitBytes = 3072;

    // Guid is char(36) on MySQL, a string is varchar(n) (255 when the model names no length), enum and int are four bytes,
    // bool is one, long and DateTimeOffset are eight.
    internal static IEnumerable<string> OverLimit(DbContext db)
    {
        foreach (var entity in db.Model.GetEntityTypes())
        {
            foreach (var index in entity.GetIndexes())
            {
                var bytes = 0;
                foreach (var property in index.Properties)
                {
                    bytes += property.ClrType switch
                    {
                        var t when t == typeof(Guid) => 36 * 4,
                        var t when t == typeof(string) => (property.GetMaxLength() ?? 255) * 4,
                        var t when t == typeof(bool) => 1,
                        var t when t == typeof(long) || t == typeof(DateTimeOffset) || t == typeof(DateTime) => 8,
                        _ => 4,
                    };
                }

                if (bytes > LimitBytes)
                    yield return $"{entity.GetTableName()}({string.Join(", ", index.Properties.Select(p => p.Name))}) = {bytes} bytes";
            }
        }
    }

    [Fact]
    public void Every_index_of_the_identity_model_fits_the_mysql_key_limit()
    {
        var options = new DbContextOptionsBuilder<ModulusIdentityDbContext>()
            .UseMySQL("Server=localhost;Database=unused;User=root;Password=unused")
            .Options;
        using var db = new ModulusIdentityDbContext(options, new NullCurrentTenant());

        OverLimit(db).Should().BeEmpty("MySQL refuses these indexes at startup");
    }
}
