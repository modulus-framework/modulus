namespace Modulus.Identity.Tests;

using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Tenancy;
using Modulus.Identity.Abstractions;
using Modulus.Identity.EntityFrameworkCore;
using Xunit;

/// <summary>
/// The lifecycle, principal-kind and suspension columns, and the append-only transition history. A row written before the columns
/// existed (an existing deployment's account) must read back as an active person with no suspension.
/// </summary>
[Trait("Category", "Unit")]
public sealed class LifecycleSchemaTests : IAsyncLifetime
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
    public async Task A_new_account_round_trips_its_kind_lifecycle_and_suspension()
    {
        var userId = Guid.NewGuid();
        var changedAt = new DateTimeOffset(2026, 10, 18, 9, 0, 0, TimeSpan.Zero);

        await using (var db = Host())
        {
            db.Users.Add(new ModulusUser
            {
                Id = userId,
                UserName = "ops@example.test",
                Email = "ops@example.test",
                PrincipalKind = "employee",
                Lifecycle = AccountLifecycle.Disabled,
                LifecycleChangedAt = changedAt,
                SuspensionReason = SuspensionReasons.AdministrativeHold,
                SuspensionNote = "Pending the audit",
            });
            await db.SaveChangesAsync();
        }

        await using var read = Host();
        var user = await read.Users.AsNoTracking().SingleAsync(u => u.Id == userId);
        user.PrincipalKind.Should().Be("employee");
        user.Lifecycle.Should().Be(AccountLifecycle.Disabled);
        user.LifecycleChangedAt.Should().Be(changedAt);
        user.SuspensionReason.Should().Be(SuspensionReasons.AdministrativeHold);
        user.SuspensionNote.Should().Be("Pending the audit");
    }

    [Fact]
    public async Task A_row_written_before_the_columns_existed_reads_back_as_an_active_person()
    {
        var userId = Guid.NewGuid();

        // The insert names only the columns an existing deployment has, so the new ones take their database defaults.
        await using (var db = Host())
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO AspNetUsers (Id, UserName, NormalizedUserName, Email, NormalizedEmail, EmailConfirmed,
                    PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnabled, AccessFailedCount, IsActive, MustChangePin)
                VALUES ({0}, 'legacy@example.test', 'LEGACY@EXAMPLE.TEST', 'legacy@example.test', 'LEGACY@EXAMPLE.TEST', 0,
                    0, 0, 1, 0, 1, 0)
                """,
                userId);
        }

        await using var read = Host();
        var user = await read.Users.AsNoTracking().SingleAsync(u => u.Id == userId);
        user.PrincipalKind.Should().Be(PrincipalKinds.Person);
        user.Lifecycle.Should().Be(AccountLifecycle.Active);
        user.LifecycleChangedAt.Should().BeNull();
        user.SuspensionReason.Should().BeNull();
    }

    [Fact]
    public async Task Transitions_are_kept_in_order_and_outlive_the_account()
    {
        var userId = Guid.NewGuid();
        var actor = Guid.NewGuid();

        await using (var db = Host())
        {
            db.AccountTransitions.Add(new ModulusAccountTransition
            {
                UserId = userId,
                FromState = AccountLifecycle.Active,
                ToState = AccountLifecycle.Disabled,
                ActorId = actor,
                Reason = SuspensionReasons.SecurityReview,
                OccurredAt = DateTimeOffset.UtcNow,
            });
            db.AccountTransitions.Add(new ModulusAccountTransition
            {
                UserId = userId,
                FromState = AccountLifecycle.Disabled,
                ToState = AccountLifecycle.Archived,
                OccurredAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        await using var read = Host();
        var history = await read.AccountTransitions.AsNoTracking().Where(t => t.UserId == userId).OrderBy(t => t.Id).ToListAsync();
        history.Select(t => (t.FromState, t.ToState)).Should().Equal(
            (AccountLifecycle.Active, AccountLifecycle.Disabled),
            (AccountLifecycle.Disabled, AccountLifecycle.Archived));
        history[0].ActorId.Should().Be(actor);
        history[1].ActorId.Should().BeNull();
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
