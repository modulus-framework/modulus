using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Authorization.Grants;
using Modulus.BackgroundJobs;
using Modulus.Core.Abstractions;
using Modulus.Identity.Abstractions;
using Modulus.Identity.EntityFrameworkCore;
using Modulus.Identity.Extensions;
using Xunit;

namespace Modulus.Identity.Tests;

/// <summary>
/// The Identity answer to the per-stage job re-check (A6-1): a step needing a permission is allowed only while the initiator may
/// still start access and holds the permission through the grant store, and it fails closed without a permission resolver.
/// </summary>
[Trait("Category", "Unit")]
public sealed class JobStageIdentityTests
{
    private const string Permission = "ops:wipe";

    private sealed class FakeEnvironment : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Modulus.Identity.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private sealed class HoldsOnly(string permission) : IPermissionResolver
    {
        public IReadOnlySet<string> Resolve(PrincipalGrantQuery principal)
            => new HashSet<string>(StringComparer.OrdinalIgnoreCase) { permission };

        public IReadOnlySet<string> Resolve(PrincipalGrantQuery principal, IReadOnlyCollection<PermissionGrant> grants) => Resolve(principal);
    }

    // Grants the permission only to a query made through the agent role, the way the agent's cap narrows a person's rights.
    private sealed class HoldsOnlyThroughAgent(string permission, string agentRole) : IPermissionResolver
    {
        public IReadOnlySet<string> Resolve(PrincipalGrantQuery principal)
            => principal.AgentRole == agentRole
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) { permission }
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlySet<string> Resolve(PrincipalGrantQuery principal, IReadOnlyCollection<PermissionGrant> grants) => Resolve(principal);
    }

    private static async Task<(ServiceProvider Provider, SqliteConnection Connection, Guid User)> CreateAsync(IPermissionResolver? resolver)
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddSingleton<Microsoft.Extensions.Hosting.IHostEnvironment>(new FakeEnvironment());
        services.AddScoped<ICurrentTenant, Modulus.Core.Null.NullCurrentTenant>();
        services.AddDbContext<ModulusIdentityDbContext>(o => o.UseSqlite(connection));
        if (resolver is not null)
            services.AddSingleton(resolver);
        services.AddModulusIdentity<ModulusIdentityDbContext, ModulusUser, ModulusRole>(new ConfigurationBuilder().Build());
        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ModulusIdentityDbContext>().Database.EnsureCreatedAsync();
        var user = new ModulusUser { UserName = "stage@example.test", Email = "stage@example.test" };
        (await scope.ServiceProvider.GetRequiredService<UserManager<ModulusUser>>().CreateAsync(user)).Succeeded.Should().BeTrue();
        return (provider, connection, user.Id);
    }

    [Fact]
    public async Task A_step_is_allowed_while_the_initiator_may_start_access_and_holds_the_permission()
    {
        var (provider, connection, user) = await CreateAsync(new HoldsOnly(Permission));
        await using var _ = provider;
        await using var __ = connection;

        using var scope = provider.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<IJobInitiatorGuard>().MayPerformAsync(user, Permission, default)).Should().BeTrue();
    }

    [Fact]
    public async Task A_step_is_refused_for_a_permission_the_initiator_does_not_hold()
    {
        var (provider, connection, user) = await CreateAsync(new HoldsOnly("ops:read"));
        await using var _ = provider;
        await using var __ = connection;

        using var scope = provider.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<IJobInitiatorGuard>().MayPerformAsync(user, Permission, default)).Should().BeFalse();
    }

    [Fact]
    public async Task A_step_fails_closed_when_no_permission_resolver_is_registered()
    {
        var (provider, connection, user) = await CreateAsync(resolver: null);
        await using var _ = provider;
        await using var __ = connection;

        using var scope = provider.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<IJobInitiatorGuard>().MayPerformAsync(user, Permission, default)).Should().BeFalse();
    }

    [Fact]
    public async Task A_step_is_refused_once_the_initiator_may_no_longer_start_access_even_with_the_permission()
    {
        var (provider, connection, user) = await CreateAsync(new HoldsOnly(Permission));
        await using var _ = provider;
        await using var __ = connection;
        using (var scope = provider.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ModulusUser>>();
            var account = (await users.FindByIdAsync(user.ToString()))!;
            account.Lifecycle = AccountLifecycle.Disabled;
            (await users.UpdateAsync(account)).Succeeded.Should().BeTrue();
        }

        using var fresh = provider.CreateScope();
        (await fresh.ServiceProvider.GetRequiredService<IJobInitiatorGuard>().MayPerformAsync(user, Permission, default)).Should().BeFalse();
    }

    [Fact]
    public async Task A_step_through_an_agent_is_capped_by_the_agent_role()
    {
        var (provider, connection, user) = await CreateAsync(new HoldsOnlyThroughAgent(Permission, "integration:tablet"));
        await using var _ = provider;
        await using var __ = connection;

        using var scope = provider.CreateScope();
        var guard = scope.ServiceProvider.GetRequiredService<IJobInitiatorGuard>();
        (await guard.MayPerformAsAgentAsync(user, "integration:tablet", Permission, default)).Should().BeTrue();
        (await guard.MayPerformAsAgentAsync(user, "integration:other", Permission, default)).Should().BeFalse("another agent's rights do not reach the step");
        (await guard.MayPerformAsync(user, Permission, default)).Should().BeFalse("the person alone does not hold it through the agent");
    }
}
