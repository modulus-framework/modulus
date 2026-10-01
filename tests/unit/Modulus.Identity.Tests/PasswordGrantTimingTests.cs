namespace Modulus.Identity.Tests;

using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.Identity.Abstractions;
using Modulus.Identity.EntityFrameworkCore;
using Xunit;

/// <summary>
/// The password grant refused an unknown user name, an inactive, unconfirmed or locked-out account before any password
/// hash ran, while a real account with a wrong password paid for one. The response time therefore told a caller which
/// user names exist. Every denial now verifies exactly one hash; the tests count calls rather than time them, which
/// would be flaky.
/// </summary>
[Trait("Category", "Unit")]
public sealed class PasswordGrantTimingTests : IAsyncDisposable
{
    private const string Password = "Correct-horse-1";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly ServiceProvider _services;
    private readonly AsyncServiceScope _scope;
    private readonly CountingHasher _hasher = new();

    public PasswordGrantTimingTests()
    {
        _connection.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddAuthentication();
        services.AddSingleton<ICurrentTenant, HostTenant>();
        services.AddDbContext<ModulusIdentityDbContext<ModulusUser, ModulusRole>>(o => o.UseSqlite(_connection));
        services.AddIdentityCore<ModulusUser>(o => o.SignIn.RequireConfirmedEmail = true)
            .AddRoles<ModulusRole>()
            .AddSignInManager()
            .AddEntityFrameworkStores<ModulusIdentityDbContext<ModulusUser, ModulusRole>>();
        services.AddSingleton<IPasswordHasher<ModulusUser>>(_hasher);

        _services = services.BuildServiceProvider();
        _scope = _services.CreateAsyncScope();
        _scope.ServiceProvider.GetRequiredService<ModulusIdentityDbContext<ModulusUser, ModulusRole>>()
            .Database.EnsureCreated();
    }

    private UserManager<ModulusUser> Users => _scope.ServiceProvider.GetRequiredService<UserManager<ModulusUser>>();

    private IdentityPasswordGrantValidator<ModulusUser> Validator => new(
        _scope.ServiceProvider.GetRequiredService<SignInManager<ModulusUser>>(), Users);

    [Fact]
    public async Task An_unknown_user_name_costs_one_password_hash()
    {
        var result = await VerifyCountingAsync("nobody");

        result.Verifications.Should().Be(1);
        result.Grant.Success.Should().BeFalse();
    }

    [Fact]
    public async Task A_wrong_password_for_a_real_account_costs_one_password_hash()
    {
        await CreateUserAsync("jdoe");

        var result = await VerifyCountingAsync("jdoe", "Wrong-password-1");

        result.Verifications.Should().Be(1);
        result.Grant.Success.Should().BeFalse();
    }

    [Fact]
    public async Task An_inactive_account_costs_one_password_hash()
    {
        await CreateUserAsync("jdoe", u => u.IsActive = false);

        (await VerifyCountingAsync("jdoe")).Verifications.Should().Be(1);
    }

    [Fact]
    public async Task An_unconfirmed_account_costs_one_password_hash()
    {
        await CreateUserAsync("jdoe", u => u.EmailConfirmed = false);

        var result = await VerifyCountingAsync("jdoe");

        result.Verifications.Should().Be(1);
        result.Grant.Success.Should().BeFalse();
    }

    [Fact]
    public async Task A_locked_out_account_costs_one_password_hash()
    {
        var user = await CreateUserAsync("jdoe");
        await Users.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddHours(1));

        var result = await VerifyCountingAsync("jdoe");

        result.Verifications.Should().Be(1);
        result.Grant.Success.Should().BeFalse();
    }

    [Fact]
    public async Task The_right_password_still_signs_in()
    {
        await CreateUserAsync("jdoe");

        var result = await VerifyCountingAsync("jdoe");

        result.Grant.Success.Should().BeTrue();
        result.Verifications.Should().Be(1);
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private async Task<(PasswordGrantResult Grant, int Verifications)> VerifyCountingAsync(
        string userName, string password = Password)
    {
        var before = _hasher.Verifications;
        var grant = await Validator.ValidateAsync(userName, password);
        return (grant, _hasher.Verifications - before);
    }

    private async Task<ModulusUser> CreateUserAsync(string userName, Action<ModulusUser>? configure = null)
    {
        var user = new ModulusUser
        {
            Id = Guid.NewGuid(),
            UserName = userName,
            Email = $"{userName}@example.test",
            EmailConfirmed = true,
            FirstName = "Jane",
            LastName = "Doe",
        };
        configure?.Invoke(user);
        (await Users.CreateAsync(user, Password)).Succeeded.Should().BeTrue();
        return user;
    }

    public async ValueTask DisposeAsync()
    {
        await _scope.DisposeAsync();
        await _services.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private sealed class CountingHasher : IPasswordHasher<ModulusUser>
    {
        private readonly PasswordHasher<ModulusUser> _inner = new();
        private int _verifications;

        public int Verifications => Volatile.Read(ref _verifications);

        public string HashPassword(ModulusUser user, string password) => _inner.HashPassword(user, password);

        public PasswordVerificationResult VerifyHashedPassword(ModulusUser user, string hashedPassword, string providedPassword)
        {
            Interlocked.Increment(ref _verifications);
            return _inner.VerifyHashedPassword(user, hashedPassword, providedPassword);
        }
    }

    private sealed class HostTenant : ICurrentTenant
    {
        public Guid? TenantId => null;

        public string? TenantSlug => null;

        public bool IsAvailable => false;

        public bool IsHost => true;

        public IDisposable Change(TenantInfo? tenant) => throw new NotSupportedException();
    }
}
