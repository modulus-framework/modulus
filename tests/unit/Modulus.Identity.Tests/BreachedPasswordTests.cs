using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Security;
using Modulus.Identity.Abstractions;
using Modulus.Identity.EntityFrameworkCore;
using Modulus.Identity.Extensions;
using Xunit;

namespace Modulus.Identity.Tests;

/// <summary>The breached-password check (A12-2): an offline list of SHA-1 hashes, applied to every path that sets a password.</summary>
[Trait("Category", "Unit")]
public sealed class BreachedPasswordTests
{
    // Meets the default password policy, and is what the list below holds.
    private const string Breached = "Winter2024!";
    private const string Fresh = "Passw0rd!x-unlisted";

    [Fact]
    public void The_list_matches_hashes_in_either_case_and_ignores_the_count_and_junk_lines()
    {
        var path = WriteList(
            $"{Sha1Hex("other-password-9")}:12",
            $"{Sha1Hex(Breached).ToLowerInvariant()}",
            "not-a-hash",
            "");
        try
        {
            var list = new FileBreachedPasswordList(path);

            list.IsBreached(Breached).Should().BeTrue();
            list.IsBreached("other-password-9").Should().BeTrue();
            list.IsBreached(Fresh).Should().BeFalse();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task A_breached_password_is_refused_on_create_and_on_change()
    {
        var path = WriteList(Sha1Hex(Breached));
        try
        {
            var (provider, connection) = await CreateAsync(enabled: true, path);
            await using var _ = provider;
            await using var __ = connection;
            using var scope = provider.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ModulusUser>>();

            var refused = await users.CreateAsync(NewUser("ann"), Breached);
            refused.Succeeded.Should().BeFalse();
            refused.Errors.Should().Contain(e => e.Code == "BreachedPassword");
            refused.Errors.Should().NotContain(e => e.Description.Contains(Breached, StringComparison.Ordinal), "the message never repeats the password");

            var user = NewUser("bob");
            (await users.CreateAsync(user, Fresh)).Succeeded.Should().BeTrue();
            var changed = await users.ChangePasswordAsync(user, Fresh, Breached);
            changed.Succeeded.Should().BeFalse();
            changed.Errors.Should().Contain(e => e.Code == "BreachedPassword");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task With_the_check_off_a_listed_password_is_accepted()
    {
        var path = WriteList(Sha1Hex(Breached));
        try
        {
            var (provider, connection) = await CreateAsync(enabled: false, path);
            await using var _ = provider;
            await using var __ = connection;
            using var scope = provider.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ModulusUser>>();

            (await users.CreateAsync(NewUser("cara"), Breached)).Succeeded.Should().BeTrue();
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static ModulusUser NewUser(string name) => new() { UserName = $"{name}@example.test", Email = $"{name}@example.test" };

    private static string Sha1Hex(string password)
        => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(password)));

    private static string WriteList(params string[] lines)
    {
        var path = Path.GetTempFileName();
        File.WriteAllLines(path, lines);
        return path;
    }

    private static async Task<(ServiceProvider Provider, SqliteConnection Connection)> CreateAsync(bool enabled, string listPath)
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Identity:BreachedPasswords:Enabled"] = enabled.ToString(),
            ["Identity:BreachedPasswords:ListPath"] = listPath,
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddSingleton<Microsoft.Extensions.Hosting.IHostEnvironment>(new FakeEnvironment());
        services.AddSingleton<IIdentityEmailSender>(new SilentSender());
        services.AddSingleton<ISecurityAuditLog>(new NullAudit());
        services.AddScoped<ICurrentTenant, Modulus.Core.Null.NullCurrentTenant>();
        services.AddDbContext<ModulusIdentityDbContext>(o => o.UseSqlite(connection));
        services.AddModulusIdentity<ModulusIdentityDbContext, ModulusUser, ModulusRole>(configuration);
        var provider = services.BuildServiceProvider();
        using (var scope = provider.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ModulusIdentityDbContext>().Database.EnsureCreatedAsync();
        return (provider, connection);
    }

    private sealed class NullAudit : ISecurityAuditLog
    {
        public void Record(SecurityAuditEvent auditEvent)
        {
        }
    }

    private sealed class SilentSender : IIdentityEmailSender
    {
        public Task SendPasswordResetEmailAsync(string email, string resetToken, CancellationToken ct = default) => Task.CompletedTask;

        public Task SendEmailConfirmationEmailAsync(string email, string confirmationToken, CancellationToken ct = default) => Task.CompletedTask;

        public Task SendInvitationEmailAsync(string email, string invitationToken, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeEnvironment : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Acme ERP";
        public string ContentRootPath { get; set; } = "/";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
