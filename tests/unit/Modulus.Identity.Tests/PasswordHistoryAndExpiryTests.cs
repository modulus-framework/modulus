using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;
using Modulus.Identity.Abstractions;
using Modulus.Identity.EntityFrameworkCore;
using Modulus.Identity.Extensions;
using Xunit;

namespace Modulus.Identity.Tests;

/// <summary>
/// The password reuse check and expiry, through the real <c>AddModulusIdentity</c> registration: every password an account
/// sets is recorded, a recent one is refused, and an expired one is refused at the password grant until it is changed.
/// </summary>
[Trait("Category", "Unit")]
public sealed class PasswordHistoryAndExpiryTests
{
    private const string First = "Alpha-One-1";
    private const string Second = "Bravo-Two-2";
    private const string Third = "Charlie-3-Three";

    [Fact]
    public async Task Reusing_a_recent_password_is_refused()
    {
        await using var h = await Harness.CreateAsync(historyCount: 3, maxAgeDays: 0);
        var user = await h.CreateUserAsync("ann", First);
        (await h.Users.ChangePasswordAsync(user, First, Second)).Succeeded.Should().BeTrue();

        var reuse = await h.Users.ChangePasswordAsync(user, Second, First);

        reuse.Succeeded.Should().BeFalse();
        reuse.Errors.Should().Contain(e => e.Code == "PasswordReused");
    }

    [Fact]
    public async Task A_password_older_than_the_reuse_window_is_accepted_again()
    {
        await using var h = await Harness.CreateAsync(historyCount: 2, maxAgeDays: 0);
        var user = await h.CreateUserAsync("ann", First);
        await h.Users.ChangePasswordAsync(user, First, Second);
        await h.Users.ChangePasswordAsync(user, Second, Third);

        // The window keeps the last two (Third, Second); First has fallen out of it.
        (await h.Users.ChangePasswordAsync(user, Third, First)).Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task Every_password_the_account_sets_is_recorded()
    {
        await using var h = await Harness.CreateAsync(historyCount: 2, maxAgeDays: 0);
        var user = await h.CreateUserAsync("ann", First);
        await h.Users.ChangePasswordAsync(user, First, Second);
        await h.Users.ChangePasswordAsync(user, Second, Third);

        (await h.Db.PasswordHistory.Where(r => r.UserId == user.Id).CountAsync()).Should().Be(3);
    }

    [Fact]
    public async Task Reuse_is_allowed_when_the_reuse_check_is_off()
    {
        await using var h = await Harness.CreateAsync(historyCount: 0, maxAgeDays: 0);
        var user = await h.CreateUserAsync("ann", First);
        await h.Users.ChangePasswordAsync(user, First, Second);

        (await h.Users.ChangePasswordAsync(user, Second, First)).Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task Old_password_rows_are_capped_per_account()
    {
        await using var h = await Harness.CreateAsync(historyCount: 0, maxAgeDays: 0);
        var user = await h.CreateUserAsync("ann", First);
        var current = First;
        for (var i = 0; i < 52; i++)
        {
            var next = $"Rotate-{i}-Pass";
            (await h.Users.ChangePasswordAsync(user, current, next)).Succeeded.Should().BeTrue();
            current = next;
        }

        (await h.Db.PasswordHistory.Where(r => r.UserId == user.Id).CountAsync()).Should().Be(50);
    }

    [Fact]
    public async Task An_expired_password_is_refused_at_the_password_grant()
    {
        await using var h = await Harness.CreateAsync(historyCount: 0, maxAgeDays: 90);
        await h.CreateUserAsync("ann", First);
        await h.AgeCurrentPasswordAsync(TimeSpan.FromDays(120));

        var grant = await h.Validator.ValidateAsync("ann", First);

        grant.Success.Should().BeFalse();
        grant.Error.Should().Be(PasswordGrantResult.PasswordExpiredError);
    }

    [Fact]
    public async Task Changing_an_expired_password_clears_the_expiry()
    {
        await using var h = await Harness.CreateAsync(historyCount: 0, maxAgeDays: 90);
        var user = await h.CreateUserAsync("ann", First);
        await h.AgeCurrentPasswordAsync(TimeSpan.FromDays(120));
        (await h.Validator.ValidateAsync("ann", First)).Error.Should().Be(PasswordGrantResult.PasswordExpiredError);

        (await h.Users.ChangePasswordAsync(user, First, Second)).Succeeded.Should().BeTrue();

        (await h.Validator.ValidateAsync("ann", Second)).Success.Should().BeTrue();
    }

    [Fact]
    public async Task A_password_within_its_age_signs_in()
    {
        await using var h = await Harness.CreateAsync(historyCount: 0, maxAgeDays: 90);
        await h.CreateUserAsync("ann", First);
        await h.AgeCurrentPasswordAsync(TimeSpan.FromDays(10));

        (await h.Validator.ValidateAsync("ann", First)).Success.Should().BeTrue();
    }

    [Fact]
    public async Task An_account_with_no_recorded_change_is_not_expired()
    {
        await using var h = await Harness.CreateAsync(historyCount: 0, maxAgeDays: 90);
        await h.CreateUserAsync("ann", First);
        await h.Db.PasswordHistory.ExecuteDeleteAsync();

        (await h.Validator.ValidateAsync("ann", First)).Success.Should().BeTrue();
    }

    [Fact]
    public async Task Deleting_an_account_removes_its_password_history()
    {
        await using var h = await Harness.CreateAsync(historyCount: 3, maxAgeDays: 0);
        var user = await h.CreateUserAsync("ann", First);
        await h.Users.ChangePasswordAsync(user, First, Second);

        (await h.Users.DeleteAsync(user)).Succeeded.Should().BeTrue();

        (await h.Db.PasswordHistory.CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData(null, 90, false)]
    [InlineData(10, 90, false)]
    [InlineData(100, 90, true)]
    [InlineData(100, 0, false)]
    public void Expiry_is_measured_from_the_last_change(int? daysAgo, int maxAgeDays, bool expired)
    {
        var now = DateTimeOffset.UtcNow;
        var changedAt = daysAgo is { } days ? now.AddDays(-days) : (DateTimeOffset?)null;

        PasswordExpiry.IsExpired(changedAt, maxAgeDays, now).Should().Be(expired);
    }

    /// <summary>One SQLite database, the real Identity registration and a scope, per test.</summary>
    private sealed class Harness : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly ServiceProvider _provider;
        private readonly AsyncServiceScope _scope;

        private Harness(SqliteConnection connection, ServiceProvider provider, AsyncServiceScope scope)
        {
            _connection = connection;
            _provider = provider;
            _scope = scope;
        }

        public static async Task<Harness> CreateAsync(int historyCount, int maxAgeDays)
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();

            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Identity:Password:HistoryCount"] = historyCount.ToString(),
                ["Identity:Password:MaxAgeDays"] = maxAgeDays.ToString(),
            }).Build();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddScoped<ICurrentTenant, Modulus.Core.Null.NullCurrentTenant>();
            services.AddDbContext<ModulusIdentityDbContext>(o => o.UseSqlite(connection));
            services.AddModulusIdentity<ModulusIdentityDbContext, ModulusUser, ModulusRole>(configuration);

            var provider = services.BuildServiceProvider();
            var scope = provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ModulusIdentityDbContext>().Database.EnsureCreatedAsync();
            return new Harness(connection, provider, scope);
        }

        public UserManager<ModulusUser> Users => _scope.ServiceProvider.GetRequiredService<UserManager<ModulusUser>>();

        public ModulusIdentityDbContext Db => _scope.ServiceProvider.GetRequiredService<ModulusIdentityDbContext>();

        public IdentityPasswordGrantValidator<ModulusUser> Validator => new(
            _scope.ServiceProvider.GetRequiredService<SignInManager<ModulusUser>>(),
            Users,
            audit: null,
            history: _scope.ServiceProvider.GetRequiredService<IPasswordHistoryStore>(),
            identityOptions: _scope.ServiceProvider.GetRequiredService<IOptions<ModulusIdentityOptions>>());

        public async Task<ModulusUser> CreateUserAsync(string userName, string password)
        {
            var user = new ModulusUser { UserName = userName, Email = $"{userName}@example.test" };
            (await Users.CreateAsync(user, password)).Succeeded.Should().BeTrue();
            return user;
        }

        /// <summary>Moves the newest recorded change back in time, as if the password had been set that long ago.</summary>
        public async Task AgeCurrentPasswordAsync(TimeSpan age)
        {
            var newest = await Db.PasswordHistory.OrderByDescending(r => r.Id).FirstAsync();
            newest.ChangedAt = DateTimeOffset.UtcNow - age;
            await Db.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await _scope.DisposeAsync();
            await _provider.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
