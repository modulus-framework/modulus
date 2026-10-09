using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulus.AuditLogging.Security;
using Modulus.Core.Abstractions;
using Modulus.Identity.Abstractions;
using Modulus.Identity.EntityFrameworkCore;
using Modulus.Identity.EntityFrameworkCore.Extensions;
using Modulus.Identity.Extensions;
using OpenIddict.Abstractions;
using Xunit;

namespace Modulus.Identity.Tests;

/// <summary>Ending a user's sessions: one, or all (security stamp plus every stored token).</summary>
[Trait("Category", "Unit")]
public sealed class UserSessionServiceTests
{
    private sealed class Recorder : IAccessChangeObserver
    {
        public List<AccessChange> Changes { get; } = [];

        public ValueTask OnAccessChangedAsync(AccessChange change, CancellationToken ct = default)
        {
            Changes.Add(change);
            return ValueTask.CompletedTask;
        }
    }

    private static async Task<(ServiceProvider Provider, Recorder Recorder, SqliteConnection Connection)> CreateAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var recorder = new Recorder();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IAccessChangeObserver>(recorder);
        services.AddScoped<ICurrentTenant, Modulus.Core.Null.NullCurrentTenant>();
        services.AddDbContext<ModulusIdentityDbContext>(o => o.UseSqlite(connection).UseOpenIddict());
        services.AddModulusIdentity<ModulusIdentityDbContext, ModulusUser, ModulusRole>(new ConfigurationBuilder().Build());
        services.AddModulusIdentityStore<ModulusIdentityDbContext>();
        var provider = services.BuildServiceProvider();
        using (var scope = provider.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ModulusIdentityDbContext>().Database.EnsureCreatedAsync();
        return (provider, recorder, connection);
    }

    private static async Task<ModulusUser> AddUserAsync(IServiceProvider sp, string name)
    {
        var users = sp.GetRequiredService<UserManager<ModulusUser>>();
        var user = new ModulusUser { UserName = name, Email = $"{name}@example.test" };
        (await users.CreateAsync(user, "Passw0rd!x")).Succeeded.Should().BeTrue();
        return user;
    }

    private static async Task AddTokenAsync(IServiceProvider sp, Guid userId, string type)
    {
        var tokens = sp.GetRequiredService<IOpenIddictTokenManager>();
        await tokens.CreateAsync(new OpenIddictTokenDescriptor
        {
            Subject = userId.ToString(),
            Type = type,
            Status = OpenIddictConstants.Statuses.Valid,
            CreationDate = DateTimeOffset.UtcNow,
            ExpirationDate = DateTimeOffset.UtcNow.AddHours(1),
        });
    }

    [Fact]
    public async Task Sessions_are_listed_and_one_can_be_revoked()
    {
        var (provider, recorder, connection) = await CreateAsync();
        await using var _ = provider;
        await using var __ = connection;
        using var scope = provider.CreateScope();
        var user = await AddUserAsync(scope.ServiceProvider, "ann");
        var other = await AddUserAsync(scope.ServiceProvider, "bob");
        await AddTokenAsync(scope.ServiceProvider, user.Id, OpenIddictConstants.TokenTypeHints.RefreshToken);
        await AddTokenAsync(scope.ServiceProvider, user.Id, OpenIddictConstants.TokenTypeHints.AccessToken);
        await AddTokenAsync(scope.ServiceProvider, other.Id, OpenIddictConstants.TokenTypeHints.RefreshToken);
        var sessions = scope.ServiceProvider.GetRequiredService<IUserSessionService>();

        var listed = await sessions.ListAsync(user.Id);
        listed.Should().HaveCount(2);
        listed.Select(s => s.Type).Should().BeEquivalentTo(
            [OpenIddictConstants.TokenTypeHints.RefreshToken, OpenIddictConstants.TokenTypeHints.AccessToken]);

        (await sessions.RevokeAsync(user.Id, listed[0].Id)).Should().BeTrue();
        (await sessions.ListAsync(user.Id)).Should().ContainSingle();
        (await sessions.ListAsync(other.Id)).Should().ContainSingle("another user's sessions are untouched");
        (await sessions.RevokeAsync(other.Id, listed[1].Id)).Should().BeFalse("a session id of another user does not match");
        recorder.Changes.Should().ContainSingle(c => c.Reason == "session.revoked");
    }

    [Fact]
    public async Task Revoking_all_changes_the_stamp_revokes_every_token_and_tells_observers()
    {
        var (provider, recorder, connection) = await CreateAsync();
        await using var _ = provider;
        await using var __ = connection;
        using var scope = provider.CreateScope();
        var user = await AddUserAsync(scope.ServiceProvider, "ann");
        await AddTokenAsync(scope.ServiceProvider, user.Id, OpenIddictConstants.TokenTypeHints.RefreshToken);
        await AddTokenAsync(scope.ServiceProvider, user.Id, OpenIddictConstants.TokenTypeHints.AccessToken);
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ModulusUser>>();
        var before = await users.GetSecurityStampAsync(user);
        var sessions = scope.ServiceProvider.GetRequiredService<IUserSessionService>();

        (await sessions.RevokeAllAsync(user.Id, "test")).Should().Be(2);

        (await users.GetSecurityStampAsync((await users.FindByIdAsync(user.Id.ToString()))!)).Should().NotBe(before);
        (await sessions.ListAsync(user.Id)).Should().BeEmpty();
        recorder.Changes.Should().ContainSingle(c => c.Reason == "sessions.revoked" && c.UserId == user.Id);
        (await sessions.RevokeAllAsync(Guid.NewGuid(), "unknown")).Should().Be(-1);
    }

    [Fact]
    public async Task Login_history_shows_the_users_own_identity_events_newest_first()
    {
        var (provider, _, connection) = await CreateAsync();
        await using var _ = provider;
        await using var __ = connection;
        using var scope = provider.CreateScope();
        var ann = await AddUserAsync(scope.ServiceProvider, "ann");
        var bob = await AddUserAsync(scope.ServiceProvider, "bob");
        var store = new InMemorySecurityAuditStore(TimeProvider.System);
        async Task Add(Guid user, string action, string outcome, string? reason = null)
            => await store.AppendAsync(new SecurityAuditEvent
            {
                Category = SecurityAuditCategories.Identity,
                Action = action,
                Outcome = outcome,
                Actor = user.ToString(),
                Details = new Dictionary<string, string?> { ["reason"] = reason, ["client"] = "web" },
            });
        await Add(ann.Id, "signin.password", SecurityAuditOutcomes.Denied, "wrong-password");
        await Add(bob.Id, "token.password", SecurityAuditOutcomes.Success);
        await Add(ann.Id, "token.password", SecurityAuditOutcomes.Success);
        var history = new LoginHistoryService<ModulusUser>(
            scope.ServiceProvider.GetRequiredService<UserManager<ModulusUser>>(),
            new SingleService<ISecurityAuditStore>(store));

        var events = await history.GetAsync(ann.Id);

        events.Select(e => (e.Action, e.Succeeded, e.Reason)).Should().Equal(
            ("token.password", true, null), ("signin.password", false, "wrong-password"));
        events.Should().OnlyContain(e => e.ClientId == "web");
        (await history.GetAsync(Guid.NewGuid())).Should().BeEmpty();
    }

    private sealed class SingleService<T>(T service) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(T) ? service : null;
    }

    [Fact]
    public async Task An_integration_client_is_bound_to_a_company_and_disabling_it_revokes_its_tokens()
    {
        var (provider, _, connection) = await CreateAsync();
        await using var _ = provider;
        await using var __ = connection;
        using var scope = provider.CreateScope();
        var apps = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        await apps.CreateAsync(new OpenIddictApplicationDescriptor { ClientId = "partner-x", ClientSecret = "s3cret-s3cret" });
        var tokens = scope.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>();
        await tokens.CreateAsync(new OpenIddictTokenDescriptor
        {
            Subject = "partner-x",
            Type = OpenIddictConstants.TokenTypeHints.AccessToken,
            Status = OpenIddictConstants.Statuses.Valid,
            CreationDate = DateTimeOffset.UtcNow,
            ExpirationDate = DateTimeOffset.UtcNow.AddHours(1),
        });
        var directory = scope.ServiceProvider.GetRequiredService<IIntegrationClientDirectory>();
        var company = Guid.NewGuid();

        (await directory.FindAsync("nobody")).Should().BeNull();
        (await directory.FindAsync("partner-x")).Should().Be(new IntegrationClientBinding("partner-x", null, null));
        (await directory.BindAsync("nobody", company, null)).Should().BeFalse();

        (await directory.BindAsync("partner-x", company, null)).Should().BeTrue();
        (await directory.FindAsync("partner-x"))!.TenantId.Should().Be(company);
        IntegrationClients.RoleFor("partner-x").Should().Be("integration:partner-x");

        (await directory.DisableAsync("partner-x")).Should().BeTrue();
        var after = (await directory.FindAsync("partner-x"))!;
        after.TenantId.Should().Be(company, "disabling keeps the binding");
        after.ValidUntil.Should().BeOnOrBefore(DateTimeOffset.UtcNow);
        var remaining = 0;
        await foreach (var token in tokens.FindBySubjectAsync("partner-x"))
            remaining += await tokens.GetStatusAsync(token) == OpenIddictConstants.Statuses.Valid ? 1 : 0;
        remaining.Should().Be(0);
    }
}
