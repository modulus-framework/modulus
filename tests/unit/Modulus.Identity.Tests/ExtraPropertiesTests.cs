namespace Modulus.Identity.Tests;

using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Entities;
using Modulus.Core.Abstractions.Tenancy;
using Modulus.Identity.Abstractions;
using Modulus.Identity.EntityFrameworkCore;
using Xunit;

/// <summary>
/// The extension values of an account (<see cref="ModulusUser.ExtraProperties"/>): they round-trip through the identity database, merge
/// instead of replacing, and an in-place change is saved. These are the values the modulus-ui register and user forms read and write.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ExtraPropertiesTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private DbContextOptions<ModulusIdentityDbContext> _options = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<ModulusIdentityDbContext>().UseSqlite(_connection).Options;

        await using var db = Host();
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public async Task A_new_account_starts_with_no_extension_values()
    {
        var userId = Guid.NewGuid();
        await using (var db = Host())
        {
            db.Users.Add(new ModulusUser { Id = userId, UserName = "plain@example.test", Email = "plain@example.test" });
            await db.SaveChangesAsync();
        }

        await using var read = Host();
        var user = await read.Users.AsNoTracking().SingleAsync(u => u.Id == userId);
        user.ExtraProperties.Should().BeEmpty();
    }

    [Fact]
    public async Task Values_round_trip_through_the_database()
    {
        var userId = Guid.NewGuid();
        await using (var db = Host())
        {
            db.Users.Add(new ModulusUser
            {
                Id = userId,
                UserName = "ext@example.test",
                Email = "ext@example.test",
                ExtraProperties = new Dictionary<string, string?> { ["CostCentre"] = "CC-17", ["Badge"] = "true" },
            });
            await db.SaveChangesAsync();
        }

        await using var read = Host();
        var user = await read.Users.AsNoTracking().SingleAsync(u => u.Id == userId);
        user.GetExtraProperty("CostCentre").Should().Be("CC-17");
        user.GetExtraProperty("Badge").Should().Be("true");
        user.GetExtraProperty("Missing").Should().BeNull();
    }

    [Fact]
    public async Task Setting_some_values_keeps_the_others_and_an_empty_value_removes_one()
    {
        var userId = Guid.NewGuid();
        await using (var db = Host())
        {
            db.Users.Add(new ModulusUser
            {
                Id = userId,
                UserName = "merge@example.test",
                Email = "merge@example.test",
                ExtraProperties = new Dictionary<string, string?> { ["Kept"] = "yes", ["Dropped"] = "old" },
            });
            await db.SaveChangesAsync();
        }

        await using (var db = Host())
        {
            var user = await db.Users.SingleAsync(u => u.Id == userId);
            user.SetExtraProperties(new Dictionary<string, string?> { ["Dropped"] = null, ["Added"] = "new" });
            await db.SaveChangesAsync();
        }

        await using var read = Host();
        var stored = await read.Users.AsNoTracking().SingleAsync(u => u.Id == userId);
        stored.ExtraProperties.Should().BeEquivalentTo(new Dictionary<string, string?> { ["Kept"] = "yes", ["Added"] = "new" });
    }

    [Fact]
    public async Task An_in_place_change_to_a_value_is_saved()
    {
        var userId = Guid.NewGuid();
        await using (var db = Host())
        {
            db.Users.Add(new ModulusUser
            {
                Id = userId,
                UserName = "inplace@example.test",
                Email = "inplace@example.test",
                ExtraProperties = new Dictionary<string, string?> { ["Bin"] = "A-1" },
            });
            await db.SaveChangesAsync();
        }

        await using (var db = Host())
        {
            var user = await db.Users.SingleAsync(u => u.Id == userId);
            user.ExtraProperties["Bin"] = "B-9";
            await db.SaveChangesAsync();
        }

        await using var read = Host();
        var stored = await read.Users.AsNoTracking().SingleAsync(u => u.Id == userId);
        stored.GetExtraProperty("Bin").Should().Be("B-9");
    }

    private ModulusIdentityDbContext Host() => new(_options, new HostTenant());

    private sealed class HostTenant : ICurrentTenant
    {
        public Guid? TenantId => null;
        public string? TenantSlug => null;
        public bool IsAvailable => false;
        public bool IsHost => true;
        public IDisposable Change(TenantInfo? tenant) => throw new NotSupportedException();
    }
}
