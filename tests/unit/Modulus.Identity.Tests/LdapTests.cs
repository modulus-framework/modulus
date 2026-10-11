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

/// <summary>Directory sign-in: the password grant against a fake LDAP server, through the real Identity registration.</summary>
[Trait("Category", "Unit")]
public sealed class LdapTests
{
    private static readonly Guid Company = Guid.NewGuid();

    [Fact]
    public async Task A_directory_user_signs_in_and_gets_an_account_with_mapped_roles()
    {
        await using var h = await Harness.CreateAsync();
        h.Directory.Add("jsmith", "S3cret!pw", subject: "guid-1", email: "j.smith@example.com", groups: ["CN=ERP Buyers,OU=Groups,DC=example,DC=com"]);

        var grant = await h.Validator.ValidateAsync("jsmith", "S3cret!pw");

        grant.Success.Should().BeTrue();
        grant.TenantId.Should().Be(Company);
        grant.Roles.Should().Equal("Buyer");
        grant.Email.Should().Be("j.smith@example.com");
        grant.UserName.Should().Be("jsmith");
        (await h.Users.Users.SingleAsync()).EmailConfirmed.Should().BeTrue();
    }

    [Fact]
    public async Task A_second_sign_in_reuses_the_linked_account_even_after_a_rename()
    {
        await using var h = await Harness.CreateAsync();
        h.Directory.Add("jsmith", "pw-1", subject: "guid-1", email: "j.smith@example.com");
        var first = await h.Validator.ValidateAsync("jsmith", "pw-1");

        // Renamed in the directory; the stable id still identifies the person.
        h.Directory.Rename("jsmith", "jsmith2");
        var second = await h.Validator.ValidateAsync("jsmith2", "pw-1");

        second.Success.Should().BeTrue();
        second.Subject.Should().Be(first.Subject);
        (await h.Users.Users.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task A_wrong_directory_password_is_refused_and_creates_no_account()
    {
        await using var h = await Harness.CreateAsync();
        h.Directory.Add("jsmith", "pw-1", subject: "guid-1", email: "j.smith@example.com");

        (await h.Validator.ValidateAsync("jsmith", "wrong")).Success.Should().BeFalse();

        (await h.Users.Users.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Wrong_directory_passwords_count_toward_the_local_lockout_and_a_locked_account_is_not_asked_again()
    {
        await using var h = await Harness.CreateAsync();
        h.Directory.Add("jsmith", "pw-1", subject: "guid-1", email: "j.smith@example.com");
        (await h.Validator.ValidateAsync("jsmith", "pw-1")).Success.Should().BeTrue();
        var attempts = h.Users.Options.Lockout.MaxFailedAccessAttempts;

        for (var i = 0; i < attempts; i++)
            (await h.Validator.ValidateAsync("jsmith", "wrong")).Success.Should().BeFalse();

        var user = await h.Users.Users.SingleAsync();
        (await h.Users.IsLockedOutAsync(user)).Should().BeTrue("the failed directory binds count toward the lock-out");

        var bindsBefore = h.Directory.BindCalls;
        (await h.Validator.ValidateAsync("jsmith", "pw-1")).Success.Should().BeFalse("a locked account is refused even with the right password");
        h.Directory.BindCalls.Should().Be(bindsBefore, "the directory is not asked while the account is locked");
    }

    [Fact]
    public async Task An_empty_password_is_refused_without_asking_the_directory()
    {
        await using var h = await Harness.CreateAsync();
        h.Directory.Add("jsmith", "pw-1", subject: "guid-1", email: "j.smith@example.com");

        (await h.Validator.ValidateAsync("jsmith", "")).Success.Should().BeFalse();

        h.Directory.BindCalls.Should().Be(0);
    }

    [Fact]
    public async Task A_user_no_directory_knows_signs_in_with_a_local_password()
    {
        await using var h = await Harness.CreateAsync();
        await h.Users.CreateAsync(new ModulusUser { UserName = "breakglass", Email = "bg@example.com", EmailConfirmed = true, TenantId = Company }, "Loc@l-Passw0rd!");

        (await h.Validator.ValidateAsync("breakglass", "Loc@l-Passw0rd!")).Success.Should().BeTrue();
    }

    [Fact]
    public async Task Local_fallback_can_be_switched_off()
    {
        await using var h = await Harness.CreateAsync(allowLocalFallback: false);
        await h.Users.CreateAsync(new ModulusUser { UserName = "breakglass", Email = "bg@example.com", EmailConfirmed = true }, "Loc@l-Passw0rd!");

        (await h.Validator.ValidateAsync("breakglass", "Loc@l-Passw0rd!")).Success.Should().BeFalse();
    }

    [Fact]
    public async Task A_directory_user_never_falls_back_to_an_old_local_password()
    {
        await using var h = await Harness.CreateAsync();
        await h.Users.CreateAsync(new ModulusUser { UserName = "jsmith", Email = "j@example.com", EmailConfirmed = true }, "Loc@l-Passw0rd!");
        h.Directory.Add("jsmith", "dir-pw", subject: "guid-1");

        (await h.Validator.ValidateAsync("jsmith", "Loc@l-Passw0rd!")).Success.Should().BeFalse();
    }

    [Fact]
    public async Task An_unreachable_directory_refuses_everyone_including_local_accounts()
    {
        await using var h = await Harness.CreateAsync();
        await h.Users.CreateAsync(new ModulusUser { UserName = "breakglass", Email = "bg@example.com", EmailConfirmed = true }, "Loc@l-Passw0rd!");
        h.Directory.Unreachable = true;

        (await h.Validator.ValidateAsync("breakglass", "Loc@l-Passw0rd!")).Success.Should().BeFalse();
    }

    [Fact]
    public async Task Roles_follow_the_groups_at_every_sign_in_and_unmapped_roles_are_left_alone()
    {
        await using var h = await Harness.CreateAsync();
        h.Directory.Add("jsmith", "pw-1", subject: "guid-1", email: "j.smith@example.com", groups: ["ERP Buyers"]);
        await h.Validator.ValidateAsync("jsmith", "pw-1");
        var user = await h.Users.FindByNameAsync("jsmith");
        await h.Users.AddToRoleAsync(user!, "Auditor"); // given by hand, not by a group

        h.Directory.SetGroups("jsmith", ["ERP Approvers"]);
        var grant = await h.Validator.ValidateAsync("jsmith", "pw-1");

        grant.Roles.Should().BeEquivalentTo(["Approver", "Auditor"]);
    }

    [Fact]
    public async Task Without_auto_provisioning_only_linked_people_get_in()
    {
        await using var h = await Harness.CreateAsync(autoProvision: false);
        h.Directory.Add("jsmith", "pw-1", subject: "guid-1", email: "j.smith@example.com");

        (await h.Validator.ValidateAsync("jsmith", "pw-1")).Success.Should().BeFalse();
        (await h.Users.Users.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task A_disabled_account_is_refused_even_with_the_right_directory_password()
    {
        await using var h = await Harness.CreateAsync();
        h.Directory.Add("jsmith", "pw-1", subject: "guid-1", email: "j.smith@example.com");
        await h.Validator.ValidateAsync("jsmith", "pw-1");
        var user = (await h.Users.FindByNameAsync("jsmith"))!;
        user.IsActive = false;
        await h.Users.UpdateAsync(user);

        (await h.Validator.ValidateAsync("jsmith", "pw-1")).Success.Should().BeFalse();
    }

    [Fact]
    public async Task An_existing_account_is_linked_by_email_only_when_the_directory_is_trusted_for_it()
    {
        await using var off = await Harness.CreateAsync(hostLevel: true);
        await off.Users.CreateAsync(new ModulusUser { UserName = "old", Email = "j.smith@example.com", EmailConfirmed = true }, "Loc@l-Passw0rd!");
        off.Directory.Add("jsmith", "pw-1", subject: "guid-1", email: "j.smith@example.com");
        (await off.Validator.ValidateAsync("jsmith", "pw-1")).Success.Should().BeFalse(); // would be a duplicate address

        await using var on = await Harness.CreateAsync(hostLevel: true, linkByEmail: true);
        var existing = new ModulusUser { UserName = "old", Email = "j.smith@example.com", EmailConfirmed = true };
        await on.Users.CreateAsync(existing, "Loc@l-Passw0rd!");
        on.Directory.Add("jsmith", "pw-1", subject: "guid-1", email: "j.smith@example.com");

        var grant = await on.Validator.ValidateAsync("jsmith", "pw-1");

        grant.Success.Should().BeTrue();
        grant.Subject.Should().Be(existing.Id.ToString());
    }

    [Fact]
    public void The_filter_escape_neutralizes_injection_characters()
    {
        LdapFilter.Escape("*)(objectClass=*").Should().Be("\\2a\\29\\28objectClass=\\2a");
        LdapFilter.Escape("a\\b\0").Should().Be("a\\5cb\\00");
        LdapFilter.Escape("plain.name").Should().Be("plain.name");
    }

    [Fact]
    public void A_directory_that_would_send_passwords_in_clear_text_is_refused_at_startup()
    {
        var options = new LdapOptions { Enabled = true, Directories = [new LdapDirectoryOptions { Name = "hq", Host = "dc1.example.com", BaseDn = "DC=example,DC=com" }] };

        options.IsValidForTest().Should().BeFalse();
        options.Directories[0].UseStartTls = true;
        options.IsValidForTest().Should().BeTrue();
    }

    private sealed class FakeDirectory : ILdapDirectoryClient
    {
        private readonly Dictionary<string, (string Password, string Subject, string? Email, List<string> Groups)> _people = new(StringComparer.OrdinalIgnoreCase);

        public bool Unreachable { get; set; }

        public int BindCalls { get; private set; }

        public void Add(string name, string password, string subject, string? email = null, IEnumerable<string>? groups = null)
            => _people[name] = (password, subject, email, [.. groups ?? []]);

        public void Rename(string from, string to)
        {
            _people[to] = _people[from];
            _people.Remove(from);
        }

        public void SetGroups(string name, IEnumerable<string> groups)
            => _people[name] = _people[name] with { Groups = [.. groups] };

        public Task<LdapUserEntry?> FindUserAsync(LdapDirectoryOptions directory, string userName, CancellationToken ct = default)
        {
            if (Unreachable)
                throw new InvalidOperationException("down");
            if (!_people.TryGetValue(userName, out var p))
                return Task.FromResult<LdapUserEntry?>(null);

            var attrs = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                [directory.SubjectAttribute] = [p.Subject],
                [directory.UserNameAttribute] = [userName],
                [directory.GroupAttribute] = p.Groups,
            };
            if (p.Email is not null)
                attrs[directory.EmailAttribute] = [p.Email];
            return Task.FromResult<LdapUserEntry?>(new LdapUserEntry($"CN={userName},OU=People,DC=example,DC=com", attrs));
        }

        public Task<bool> BindAsync(LdapDirectoryOptions directory, string distinguishedName, string password, CancellationToken ct = default)
        {
            BindCalls++;
            var name = distinguishedName.Split(',')[0][3..];
            return Task.FromResult(_people.TryGetValue(name, out var p) && p.Password == password);
        }
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly ServiceProvider _provider;
        private readonly AsyncServiceScope _scope;

        private Harness(SqliteConnection connection, ServiceProvider provider, AsyncServiceScope scope, FakeDirectory directory)
        {
            _connection = connection;
            _provider = provider;
            _scope = scope;
            Directory = directory;
        }

        public FakeDirectory Directory { get; }

        public static async Task<Harness> CreateAsync(bool allowLocalFallback = true, bool autoProvision = true, bool linkByEmail = false, bool hostLevel = false)
        {
            Guid? tenant = hostLevel ? null : Company;
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();

            var values = new Dictionary<string, string?>
            {
                ["Identity:Ldap:Enabled"] = "true",
                ["Identity:Ldap:AllowLocalFallback"] = allowLocalFallback.ToString(),
                ["Identity:Ldap:Directories:0:Name"] = "hq",
                ["Identity:Ldap:Directories:0:Host"] = "dc1.example.com",
                ["Identity:Ldap:Directories:0:UseSsl"] = "true",
                ["Identity:Ldap:Directories:0:BaseDn"] = "DC=example,DC=com",
                ["Identity:Ldap:Directories:0:AutoProvision"] = autoProvision.ToString(),
                ["Identity:Ldap:Directories:0:LinkExistingByEmail"] = linkByEmail.ToString(),
                ["Identity:Ldap:Directories:0:GroupRoles:CN=ERP Buyers,OU=Groups,DC=example,DC=com"] = "Buyer",
                ["Identity:Ldap:Directories:0:GroupRoles:ERP Buyers"] = "Buyer",
                ["Identity:Ldap:Directories:0:GroupRoles:ERP Approvers"] = "Approver",
            };
            if (tenant is { } company)
                values["Identity:Ldap:Directories:0:TenantId"] = company.ToString();
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

            var directory = new FakeDirectory();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddScoped<ICurrentTenant, Modulus.Core.Null.NullCurrentTenant>();
            services.AddDbContext<ModulusIdentityDbContext>(o => o.UseSqlite(connection));
            services.AddModulusIdentity<ModulusIdentityDbContext, ModulusUser, ModulusRole>(configuration);
            services.AddSingleton<ILdapDirectoryClient>(directory);
            services.AddModulusLdap<ModulusUser>(configuration);

            var provider = services.BuildServiceProvider();
            var scope = provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ModulusIdentityDbContext>().Database.EnsureCreatedAsync();
            var harness = new Harness(connection, provider, scope, directory);
            foreach (var role in new[] { "Buyer", "Approver", "Auditor" })
                await harness.Roles.CreateAsync(new ModulusRole { Name = role, TenantId = tenant });
            return harness;
        }

        public UserManager<ModulusUser> Users => _scope.ServiceProvider.GetRequiredService<UserManager<ModulusUser>>();

        public RoleManager<ModulusRole> Roles => _scope.ServiceProvider.GetRequiredService<RoleManager<ModulusRole>>();

        public IPasswordGrantCredentialValidator Validator => _scope.ServiceProvider.GetRequiredService<IPasswordGrantCredentialValidator>();

        public async ValueTask DisposeAsync()
        {
            await _scope.DisposeAsync();
            await _provider.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}

internal static class LdapOptionsTestExtensions
{
    public static bool IsValidForTest(this LdapOptions options) => options.IsValid(out _);
}
