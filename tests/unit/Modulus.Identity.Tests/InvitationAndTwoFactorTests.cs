using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.Identity.Abstractions;
using Modulus.Identity.EntityFrameworkCore;
using Modulus.Identity.Extensions;
using Xunit;

namespace Modulus.Identity.Tests;

/// <summary>Invitations (account without a password, one-time link) and authenticator-app two-factor on the password grant.</summary>
[Trait("Category", "Unit")]
public sealed class InvitationAndTwoFactorTests
{
    private sealed class CapturingSender : IIdentityEmailSender
    {
        public List<(string Kind, string Email, string Token)> Sent { get; } = [];

        public Task SendPasswordResetEmailAsync(string email, string resetToken, CancellationToken ct = default)
        {
            Sent.Add(("reset", email, resetToken));
            return Task.CompletedTask;
        }

        public Task SendEmailConfirmationEmailAsync(string email, string confirmationToken, CancellationToken ct = default)
        {
            Sent.Add(("confirm", email, confirmationToken));
            return Task.CompletedTask;
        }

        public Task SendInvitationEmailAsync(string email, string invitationToken, CancellationToken ct = default)
        {
            Sent.Add(("invite", email, invitationToken));
            return Task.CompletedTask;
        }
    }

    private static async Task<(ServiceProvider Provider, CapturingSender Sender, SqliteConnection Connection)> CreateAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var sender = new CapturingSender();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddSingleton<Microsoft.Extensions.Hosting.IHostEnvironment>(new FakeEnvironment());
        services.AddSingleton<IIdentityEmailSender>(sender);
        services.AddScoped<ICurrentTenant, Modulus.Core.Null.NullCurrentTenant>();
        services.AddDbContext<ModulusIdentityDbContext>(o => o.UseSqlite(connection));
        services.AddModulusIdentity<ModulusIdentityDbContext, ModulusUser, ModulusRole>(new ConfigurationBuilder().Build());
        var provider = services.BuildServiceProvider();
        using (var scope = provider.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ModulusIdentityDbContext>().Database.EnsureCreatedAsync();
        return (provider, sender, connection);
    }

    private sealed class FakeEnvironment : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Acme ERP";
        public string ContentRootPath { get; set; } = "/";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    // RFC 6238 (HMAC-SHA1, 30 s, 6 digits) from the base32 key, the algorithm authenticator apps use.
    private static string Totp(string base32Key)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bits = string.Concat(base32Key.ToUpperInvariant().Where(alphabet.Contains).Select(c => Convert.ToString(alphabet.IndexOf(c), 2).PadLeft(5, '0')));
        var key = Enumerable.Range(0, bits.Length / 8).Select(i => Convert.ToByte(bits.Substring(i * 8, 8), 2)).ToArray();
        var step = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        var counter = BitConverter.GetBytes(step);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(counter);
        var hash = HMACSHA1.HashData(key, counter);
        var offset = hash[^1] & 0xF;
        var code = (((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3]) % 1_000_000;
        return code.ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    [Fact]
    public async Task An_invitation_creates_a_passwordless_account_that_the_link_activates()
    {
        var (provider, sender, connection) = await CreateAsync();
        await using var _ = provider;
        await using var __ = connection;
        using var scope = provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<RoleManager<ModulusRole>>().CreateAsync(new ModulusRole { Name = "Buyer" });
        var invitations = scope.ServiceProvider.GetRequiredService<IUserInvitationService>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ModulusUser>>();

        var result = await invitations.InviteAsync(new UserInvitation("new.user@example.test", ["Buyer"], FirstName: "New"));

        result.Succeeded.Should().BeTrue();
        var user = (await users.FindByIdAsync(result.UserId!.Value.ToString()))!;
        (await users.HasPasswordAsync(user)).Should().BeFalse("an invited account cannot sign in until the link is used");
        (await users.IsInRoleAsync(user, "Buyer")).Should().BeTrue();
        var mail = sender.Sent.Should().ContainSingle().Which;
        mail.Kind.Should().Be("invite");

        (await users.ResetPasswordAsync(user, mail.Token, "Passw0rd!x")).Succeeded.Should().BeTrue();
        (await users.HasPasswordAsync(user)).Should().BeTrue();

        // Once it has a password the address is an account, not an invitation.
        (await invitations.InviteAsync(new UserInvitation("new.user@example.test"))).Error.Should().Contain("already has an account");
    }

    [Fact]
    public async Task A_pending_invitation_can_be_resent_and_bulk_results_are_per_row()
    {
        var (provider, sender, connection) = await CreateAsync();
        await using var _ = provider;
        await using var __ = connection;
        using var scope = provider.CreateScope();
        var invitations = scope.ServiceProvider.GetRequiredService<IUserInvitationService>();

        var results = await invitations.InviteManyAsync(
        [
            new UserInvitation("a@example.test"),
            new UserInvitation("not-an-address"),
            new UserInvitation("b@example.test", ["Missing"]),
            new UserInvitation("a@example.test"),
        ]);

        results.Select(r => r.Succeeded).Should().Equal(true, false, false, true);
        results[2].Error.Should().Contain("Missing");
        sender.Sent.Select(m => m.Email).Should().Equal("a@example.test", "a@example.test");
        results[0].UserId.Should().Be(results[3].UserId, "the same pending account is re-invited, not duplicated");
    }

    [Fact]
    public async Task Two_factor_is_enforced_on_the_password_grant_and_recovery_codes_work_once()
    {
        var (provider, _, connection) = await CreateAsync();
        await using var _ = provider;
        await using var __ = connection;
        using var scope = provider.CreateScope();
        // The sign-in manager reads the request (is this browser remembered for two-factor?), as it always has one in the token endpoint.
        provider.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>().HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext { RequestServices = scope.ServiceProvider };
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ModulusUser>>();
        var user = new ModulusUser { UserName = "ann", Email = "ann@example.test" };
        (await users.CreateAsync(user, "Passw0rd!x")).Succeeded.Should().BeTrue();
        var twoFactor = scope.ServiceProvider.GetRequiredService<IUserTwoFactorService>();
        var validator = scope.ServiceProvider.GetRequiredService<IPasswordGrantCredentialValidator>();

        (await validator.ValidateWithSecondFactorAsync("ann", "Passw0rd!x", null)).Success.Should().BeTrue("two-factor is off");

        var setup = (await twoFactor.BeginSetupAsync(user.Id))!;
        setup.AuthenticatorUri.Should().StartWith("otpauth://totp/Acme%20ERP:");
        (await twoFactor.IsEnabledAsync(user.Id)).Should().BeFalse("nothing is enforced before the app is confirmed");
        (await twoFactor.EnableAsync(user.Id, "000000")).Should().BeNull("a wrong code does not turn it on");
        var key = setup.SharedKey.Replace(" ", "", StringComparison.Ordinal);
        var recovery = (await twoFactor.EnableAsync(user.Id, Totp(key)))!;
        recovery.Should().HaveCount(10);

        var missing = await validator.ValidateWithSecondFactorAsync("ann", "Passw0rd!x", null);
        missing.Success.Should().BeFalse();
        missing.Error.Should().Be(PasswordGrantResult.MfaRequiredError);
        (await validator.ValidateWithSecondFactorAsync("ann", "wrong", null)).Error.Should().Be("invalid_grant", "a wrong password never reveals the second factor");
        (await validator.ValidateWithSecondFactorAsync("ann", "Passw0rd!x", "000000")).Success.Should().BeFalse();
        (await validator.ValidateWithSecondFactorAsync("ann", "Passw0rd!x", Totp(key))).Success.Should().BeTrue();
        (await validator.ValidateWithSecondFactorAsync("ann", "Passw0rd!x", recovery[0])).Success.Should().BeTrue();
        (await validator.ValidateWithSecondFactorAsync("ann", "Passw0rd!x", recovery[0])).Success.Should().BeFalse("a recovery code is single use");

        (await twoFactor.DisableAsync(user.Id)).Should().BeTrue();
        (await validator.ValidateWithSecondFactorAsync("ann", "Passw0rd!x", null)).Success.Should().BeTrue();
    }
}
