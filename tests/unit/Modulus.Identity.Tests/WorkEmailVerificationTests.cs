namespace Modulus.Identity.Tests;

using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;
using Modulus.Identity.Abstractions;
using Modulus.Identity.EntityFrameworkCore;
using Modulus.MultiTenancy;
using Xunit;

/// <summary>
/// The work-e-mail confirmation (premodule 3): an address becomes the work e-mail only when its owner follows the link; the link is
/// single-use, short-lived, bound to the account and the pending address, replaced by a newer one, and cannot be a link of another purpose.
/// The sign-in e-mail never changes.
/// </summary>
[Trait("Category", "Unit")]
public sealed class WorkEmailVerificationTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private ServiceProvider _provider = null!;
    private AsyncServiceScope _scope;
    private FakeClock _clock = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();
        _clock = new FakeClock(new DateTimeOffset(2026, 10, 24, 9, 0, 0, TimeSpan.Zero));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddSingleton<ICurrentTenant>(new CurrentTenant());
        services.AddSingleton<TimeProvider>(_clock);
        services.AddDbContext<ModulusIdentityDbContext>(o => o.UseSqlite(_connection));
        services.AddIdentityCore<ModulusUser>().AddEntityFrameworkStores<ModulusIdentityDbContext>().AddDefaultTokenProviders();
        services.Configure<WorkEmailVerificationOptions>(_ => { });
        services.AddScoped(typeof(WorkEmailVerifier<>));
        _provider = services.BuildServiceProvider();

        using (_provider.GetRequiredService<ICurrentTenant>().Change(null))
        {
            await using var scope = _provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ModulusIdentityDbContext>().Database.EnsureCreatedAsync();
        }

        _scope = _provider.CreateAsyncScope();
    }

    public async Task DisposeAsync()
    {
        await _scope.DisposeAsync();
        await _provider.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private UserManager<ModulusUser> Users => _scope.ServiceProvider.GetRequiredService<UserManager<ModulusUser>>();

    private WorkEmailVerifier<ModulusUser> Verifier => _scope.ServiceProvider.GetRequiredService<WorkEmailVerifier<ModulusUser>>();

    [Fact]
    public async Task A_new_address_is_pending_and_is_not_the_work_email_until_its_link_is_followed()
    {
        using var host = _provider.GetRequiredService<ICurrentTenant>().Change(null);
        var ada = await CreateAsync("ada@example.com");

        var request = await Verifier.RequestAsync(ada, "ada@work.example");

        request.Outcome.Should().Be(WorkEmailRequestOutcome.Sent);
        request.Token.Should().NotBeNullOrEmpty();
        var stored = await Users.FindByIdAsync(ada.Id.ToString());
        stored!.WorkEmail.Should().BeNull("an administrator's or the person's entry is pending until confirmed");
        stored.PendingWorkEmail.Should().Be("ada@work.example");
        stored.Email.Should().Be("ada@example.com", "the sign-in address is never touched");
    }

    [Fact]
    public async Task Following_the_link_makes_the_address_the_verified_work_email_and_the_link_cannot_be_reused()
    {
        using var host = _provider.GetRequiredService<ICurrentTenant>().Change(null);
        var ada = await CreateAsync("ada@example.com");
        var token = (await Verifier.RequestAsync(ada, "ada@work.example")).Token!;

        var confirmed = await Verifier.ConfirmAsync(ada.Id, token);

        confirmed.Should().Be(WorkEmailConfirmOutcome.Verified);
        var stored = await Users.FindByIdAsync(ada.Id.ToString());
        stored!.WorkEmail.Should().Be("ada@work.example");
        stored.PendingWorkEmail.Should().BeNull();
        stored.Email.Should().Be("ada@example.com");
        (await Verifier.ConfirmAsync(ada.Id, token)).Should().Be(WorkEmailConfirmOutcome.Used, "a link works once");
    }

    [Fact]
    public async Task A_newer_request_replaces_the_earlier_link_even_for_the_same_address()
    {
        using var host = _provider.GetRequiredService<ICurrentTenant>().Change(null);
        var ada = await CreateAsync("ada@example.com");
        var first = (await Verifier.RequestAsync(ada, "ada@work.example")).Token!;
        _clock.Advance(TimeSpan.FromMinutes(2));
        var second = (await Verifier.RequestAsync(ada, "ada@work.example")).Token!;

        (await Verifier.ConfirmAsync(ada.Id, first)).Should().Be(WorkEmailConfirmOutcome.Superseded, "only the newest link works");
        (await Verifier.ConfirmAsync(ada.Id, second)).Should().Be(WorkEmailConfirmOutcome.Verified);
    }

    [Fact]
    public async Task Changing_the_pending_address_invalidates_the_link_for_the_old_one()
    {
        using var host = _provider.GetRequiredService<ICurrentTenant>().Change(null);
        var ada = await CreateAsync("ada@example.com");
        var forOld = (await Verifier.RequestAsync(ada, "old@work.example")).Token!;
        _clock.Advance(TimeSpan.FromMinutes(2));
        await Verifier.RequestAsync(ada, "new@work.example");

        (await Verifier.ConfirmAsync(ada.Id, forOld)).Should().Be(WorkEmailConfirmOutcome.Superseded);
        (await Users.FindByIdAsync(ada.Id.ToString()))!.WorkEmail.Should().BeNull();
    }

    [Fact]
    public async Task A_link_names_its_account_so_another_signed_in_person_cannot_use_it()
    {
        using var host = _provider.GetRequiredService<ICurrentTenant>().Change(null);
        var ada = await CreateAsync("ada@example.com");
        var grace = await CreateAsync("grace@example.com");
        var token = (await Verifier.RequestAsync(ada, "ada@work.example")).Token!;

        (await Verifier.ConfirmAsync(grace.Id, token)).Should().Be(WorkEmailConfirmOutcome.Invalid);
        (await Users.FindByIdAsync(ada.Id.ToString()))!.WorkEmail.Should().BeNull();
    }

    [Fact]
    public async Task A_link_of_another_purpose_cannot_confirm_a_work_email()
    {
        using var host = _provider.GetRequiredService<ICurrentTenant>().Change(null);
        var ada = await CreateAsync("ada@example.com");
        await Verifier.RequestAsync(ada, "ada@work.example");
        var protector = _scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("Modulus.Identity.EmailChange.v1").ToTimeLimitedDataProtector();
        var forged = protector.Protect($"{ada.Id:D}|anything|ada@work.example", _clock.GetUtcNow().AddMinutes(30));

        (await Verifier.ConfirmAsync(ada.Id, forged)).Should().Be(WorkEmailConfirmOutcome.Invalid, "the purpose is part of the protection");
    }

    [Fact]
    public async Task A_link_past_its_lifetime_is_refused()
    {
        using var host = _provider.GetRequiredService<ICurrentTenant>().Change(null);
        var ada = await CreateAsync("ada@example.com");
        // A lifetime already over when the link is made: the protection itself expires it.
        var expired = new WorkEmailVerifier<ModulusUser>(
            Users,
            _scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>(),
            Options.Create(new WorkEmailVerificationOptions { Lifetime = TimeSpan.FromSeconds(-1) }),
            _clock);
        var token = (await expired.RequestAsync(ada, "ada@work.example")).Token!;

        (await Verifier.ConfirmAsync(ada.Id, token)).Should().Be(WorkEmailConfirmOutcome.Expired);
    }

    [Fact]
    public void The_default_lifetime_is_thirty_minutes_and_the_cooldown_one_minute()
    {
        var defaults = new WorkEmailVerificationOptions();

        defaults.Lifetime.Should().Be(TimeSpan.FromMinutes(30));
        defaults.Cooldown.Should().Be(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Requests_inside_the_cooldown_are_refused_and_send_nothing()
    {
        using var host = _provider.GetRequiredService<ICurrentTenant>().Change(null);
        var ada = await CreateAsync("ada@example.com");
        await Verifier.RequestAsync(ada, "ada@work.example");
        _clock.Advance(TimeSpan.FromSeconds(30));

        var again = await Verifier.RequestAsync(ada, "other@work.example");

        again.Outcome.Should().Be(WorkEmailRequestOutcome.TooSoon);
        again.Token.Should().BeNull();

        _clock.Advance(TimeSpan.FromSeconds(60));
        (await Verifier.RequestAsync(ada, "other@work.example")).Outcome.Should().Be(WorkEmailRequestOutcome.Sent);
    }

    [Fact]
    public async Task Confirming_the_address_already_verified_sends_nothing()
    {
        using var host = _provider.GetRequiredService<ICurrentTenant>().Change(null);
        var ada = await CreateAsync("ada@example.com");
        var token = (await Verifier.RequestAsync(ada, "ada@work.example")).Token!;
        await Verifier.ConfirmAsync(ada.Id, token);
        _clock.Advance(TimeSpan.FromMinutes(5));

        var same = await Verifier.RequestAsync(await Users.FindByIdAsync(ada.Id.ToString()) ?? ada, "ada@work.example");

        same.Outcome.Should().Be(WorkEmailRequestOutcome.Unchanged);
    }

    [Fact]
    public async Task Clearing_withdraws_the_pending_address_and_every_link_out()
    {
        using var host = _provider.GetRequiredService<ICurrentTenant>().Change(null);
        var ada = await CreateAsync("ada@example.com");
        var token = (await Verifier.RequestAsync(ada, "ada@work.example")).Token!;

        await Verifier.ClearAsync(ada);

        (await Verifier.ConfirmAsync(ada.Id, token)).Should().Be(WorkEmailConfirmOutcome.Superseded, "a withdrawn address is not a used link");
        (await Users.FindByIdAsync(ada.Id.ToString()))!.PendingWorkEmail.Should().BeNull();
    }

    [Fact]
    public async Task A_verified_work_email_is_kept_until_a_new_one_is_confirmed()
    {
        using var host = _provider.GetRequiredService<ICurrentTenant>().Change(null);
        var ada = await CreateAsync("ada@example.com");
        await Verifier.ConfirmAsync(ada.Id, (await Verifier.RequestAsync(ada, "old@work.example")).Token!);
        _clock.Advance(TimeSpan.FromMinutes(2));

        var pending = await Verifier.RequestAsync(ada, "new@work.example");

        pending.Outcome.Should().Be(WorkEmailRequestOutcome.Sent);
        var stored = await Users.FindByIdAsync(ada.Id.ToString());
        stored!.WorkEmail.Should().Be("old@work.example", "the verified address stays until the new one is confirmed");
        stored.PendingWorkEmail.Should().Be("new@work.example");

        (await Verifier.ConfirmAsync(ada.Id, pending.Token!)).Should().Be(WorkEmailConfirmOutcome.Verified);
        (await Users.FindByIdAsync(ada.Id.ToString()))!.WorkEmail.Should().Be("new@work.example");
    }

    [Fact]
    public async Task Too_many_failed_attempts_refuse_even_the_right_link_until_a_new_request()
    {
        using var host = _provider.GetRequiredService<ICurrentTenant>().Change(null);
        var ada = await CreateAsync("ada@example.com");
        var token = (await Verifier.RequestAsync(ada, "ada@work.example")).Token!;
        for (var i = 0; i < 5; i++)
            (await Verifier.ConfirmAsync(ada.Id, "not-a-link")).Should().Be(WorkEmailConfirmOutcome.Invalid);

        (await Verifier.ConfirmAsync(ada.Id, token)).Should().Be(WorkEmailConfirmOutcome.TooManyAttempts);

        _clock.Advance(TimeSpan.FromMinutes(2));
        var again = (await Verifier.RequestAsync(ada, "ada@work.example")).Token!;
        (await Verifier.ConfirmAsync(ada.Id, again)).Should().Be(WorkEmailConfirmOutcome.Verified, "a new request clears the count");
    }

    [Fact]
    public void A_native_library_failure_is_refused_as_unavailable_not_stored_unchecked()
    {
        var (status, code, _) = ProfilePhotoRules.Describe(ProfilePhotoRefusal.ProcessingUnavailable);

        status.Should().Be(503);
        code.Should().Be("PHOTO_PROCESSING_UNAVAILABLE");
    }

    private async Task<ModulusUser> CreateAsync(string email)
    {
        var user = new ModulusUser { UserName = email.Split('@')[0], Email = email, IsActive = true, TenantId = null };
        (await Users.CreateAsync(user, "Passw0rd!x")).Succeeded.Should().BeTrue();
        return user;
    }

    /// <summary>A clock the test moves forward, for the cooldown.</summary>
    private sealed class FakeClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
