using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulus.BackgroundJobs;
using Modulus.Core.Abstractions;
using Modulus.Identity.Abstractions;
using Modulus.Identity.EntityFrameworkCore;
using Modulus.Identity.Extensions;
using Xunit;

namespace Modulus.Identity.Tests;

/// <summary>
/// Lifecycle enforcement on the user manager: a changed lifecycle must be a legal transition, the legacy <c>IsActive</c> flag can
/// only move an account between active and disabled, and every change is recorded in the transition history.
/// </summary>
[Trait("Category", "Unit")]
public sealed class LifecycleEnforcementTests
{
    [Theory]
    [InlineData(null, AccountLifecycle.Invited, false, AccountLifecycle.Invited)]
    [InlineData(null, AccountLifecycle.Active, false, AccountLifecycle.Disabled)]
    [InlineData(null, AccountLifecycle.Active, true, AccountLifecycle.Active)]
    [InlineData(AccountLifecycle.Active, AccountLifecycle.Active, false, AccountLifecycle.Disabled)]
    [InlineData(AccountLifecycle.Disabled, AccountLifecycle.Disabled, true, AccountLifecycle.Active)]
    [InlineData(AccountLifecycle.Invited, AccountLifecycle.Invited, true, AccountLifecycle.Invited)]
    [InlineData(AccountLifecycle.Archived, AccountLifecycle.Archived, true, AccountLifecycle.Archived)]
    [InlineData(AccountLifecycle.PendingActivation, AccountLifecycle.Active, true, AccountLifecycle.Active)]
    public void A_flag_moves_an_account_only_between_active_and_disabled(
        AccountLifecycle? stored, AccountLifecycle lifecycle, bool isActive, AccountLifecycle expected)
        => LifecycleReconciliation.Resolve(stored, lifecycle, isActive).Should().Be(expected);

    [Theory]
    [InlineData(AccountLifecycle.Archived, AccountLifecycle.Active)]
    [InlineData(AccountLifecycle.Invited, AccountLifecycle.Active)]
    [InlineData(AccountLifecycle.Disabled, AccountLifecycle.PendingActivation)]
    [InlineData(AccountLifecycle.Active, AccountLifecycle.Archived)]
    public void An_illegal_lifecycle_change_is_refused(AccountLifecycle stored, AccountLifecycle requested)
        => LifecycleReconciliation.Resolve(stored, requested, isActive: true).Should().BeNull();

    [Fact]
    public async Task Inviting_then_activating_records_each_step_and_keeps_the_flag_in_step()
    {
        var (provider, connection) = await CreateAsync();
        await using var _ = provider;
        await using var __ = connection;
        using var scope = provider.CreateScope();
        var invitations = scope.ServiceProvider.GetRequiredService<IUserInvitationService>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ModulusUser>>();

        var invited = await invitations.InviteAsync(new UserInvitation("joiner@example.test"));
        var user = (await users.FindByIdAsync(invited.UserId!.Value.ToString()))!;
        user.Lifecycle.Should().Be(AccountLifecycle.PendingActivation);
        user.IsActive.Should().BeFalse("a pending account cannot sign in");

        user.Lifecycle = AccountLifecycle.Active;
        (await users.UpdateAsync(user)).Succeeded.Should().BeTrue();

        var reloaded = (await users.FindByIdAsync(user.Id.ToString()))!;
        reloaded.Lifecycle.Should().Be(AccountLifecycle.Active);
        reloaded.IsActive.Should().BeTrue();
        var history = await Transitions(provider, user.Id);
        history.Select(t => (t.FromState, t.ToState)).Should().Equal(
            (AccountLifecycle.Invited, AccountLifecycle.PendingActivation),
            (AccountLifecycle.PendingActivation, AccountLifecycle.Active));
    }

    [Fact]
    public async Task Disabling_through_the_legacy_flag_moves_the_lifecycle_and_records_the_reason()
    {
        var (provider, connection) = await CreateAsync();
        await using var _ = provider;
        await using var __ = connection;
        using var scope = provider.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ModulusUser>>();
        var user = new ModulusUser { UserName = "legacy@example.test", Email = "legacy@example.test" };
        (await users.CreateAsync(user)).Succeeded.Should().BeTrue();

        user.IsActive = false;
        user.SuspensionReason = SuspensionReasons.SecurityReview;
        (await users.UpdateAsync(user)).Succeeded.Should().BeTrue();

        var reloaded = (await users.FindByIdAsync(user.Id.ToString()))!;
        reloaded.Lifecycle.Should().Be(AccountLifecycle.Disabled);
        var transition = (await Transitions(provider, user.Id)).Should().ContainSingle().Which;
        transition.FromState.Should().Be(AccountLifecycle.Active);
        transition.ToState.Should().Be(AccountLifecycle.Disabled);
        transition.Reason.Should().Be(SuspensionReasons.SecurityReview);
    }

    [Fact]
    public async Task An_archived_account_cannot_be_reactivated_by_setting_the_lifecycle_directly()
    {
        var (provider, connection) = await CreateAsync();
        await using var _ = provider;
        await using var __ = connection;
        using var scope = provider.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ModulusUser>>();
        var user = new ModulusUser { UserName = "leaver@example.test", Email = "leaver@example.test" };
        (await users.CreateAsync(user)).Succeeded.Should().BeTrue();
        user.IsActive = false;
        (await users.UpdateAsync(user)).Succeeded.Should().BeTrue();
        user.Lifecycle = AccountLifecycle.Archived;
        (await users.UpdateAsync(user)).Succeeded.Should().BeTrue();

        user.Lifecycle = AccountLifecycle.Active;
        var result = await users.UpdateAsync(user);

        result.Succeeded.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == "IllegalLifecycleTransition");
        await using (var fresh = provider.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope())
            (await fresh.ServiceProvider.GetRequiredService<ModulusIdentityDbContext>().Users.AsNoTracking().SingleAsync(u => u.Id == user.Id))
                .Lifecycle.Should().Be(AccountLifecycle.Archived, "the stored state is unchanged; the in-memory copy is discarded by the caller");
        (await Transitions(provider, user.Id)).Should().HaveCount(2, "only the two legal moves in setup are recorded; the refused one is not");
    }

    [Fact]
    public async Task Sign_in_refuses_a_disabled_lifecycle_even_when_the_flag_says_active()
    {
        const string password = "Lifecycle-sign-in-42!";
        var (provider, connection) = await CreateAsync();
        await using var _ = provider;
        await using var __ = connection;
        using var scope = provider.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ModulusUser>>();
        var validator = scope.ServiceProvider.GetRequiredService<IPasswordGrantCredentialValidator>();
        var user = new ModulusUser { UserName = "flagged@example.test", Email = "flagged@example.test" };
        (await users.CreateAsync(user, password)).Succeeded.Should().BeTrue();
        (await validator.ValidateAsync(user.UserName!, password)).Success.Should().BeTrue("the control: an active account signs in");

        // A row where the lifecycle says disabled but the flag still says active (written around the user manager).
        await scope.ServiceProvider.GetRequiredService<ModulusIdentityDbContext>().Users
            .Where(u => u.Id == user.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.Lifecycle, AccountLifecycle.Disabled).SetProperty(u => u.IsActive, true));

        // A fresh scope: the update bypasses the change tracker, which still holds the account read above.
        using var fresh = provider.CreateScope();
        (await fresh.ServiceProvider.GetRequiredService<IPasswordGrantCredentialValidator>().ValidateAsync(user.UserName!, password)).Success.Should().BeFalse("a disabled lifecycle refuses even when the flag says active");
    }

    [Fact]
    public async Task A_queued_job_stops_running_once_its_initiator_is_disabled()
    {
        var (provider, connection) = await CreateAsync();
        await using var _ = provider;
        await using var __ = connection;
        Guid id;
        using (var scope = provider.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ModulusUser>>();
            var user = new ModulusUser { UserName = "queued@example.test", Email = "queued@example.test" };
            (await users.CreateAsync(user)).Succeeded.Should().BeTrue();
            id = user.Id;
            (await scope.ServiceProvider.GetRequiredService<IJobInitiatorGuard>().MayRunAsync(id, default)).Should().BeTrue();
        }

        using (var scope = provider.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ModulusUser>>();
            var user = (await users.FindByIdAsync(id.ToString()))!;
            user.Lifecycle = AccountLifecycle.Disabled;
            (await users.UpdateAsync(user)).Succeeded.Should().BeTrue();
        }

        using var fresh = provider.CreateScope();
        (await fresh.ServiceProvider.GetRequiredService<IJobInitiatorGuard>().MayRunAsync(id, default)).Should().BeFalse();
    }

    [Fact]
    public async Task The_role_directory_says_a_disabled_or_unknown_account_may_not_start_access()
    {
        var (provider, connection) = await CreateAsync();
        await using var _ = provider;
        await using var __ = connection;
        Guid id;
        using (var scope = provider.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ModulusUser>>();
            var user = new ModulusUser { UserName = "directory@example.test", Email = "directory@example.test" };
            (await users.CreateAsync(user)).Succeeded.Should().BeTrue();
            id = user.Id;
            (await scope.ServiceProvider.GetRequiredService<IUserRoleDirectory>().MayStartAccessAsync(id)).Should().BeTrue();
        }

        using (var scope = provider.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ModulusUser>>();
            var user = (await users.FindByIdAsync(id.ToString()))!;
            user.IsActive = false;
            (await users.UpdateAsync(user)).Succeeded.Should().BeTrue();
        }

        using var fresh = provider.CreateScope();
        var directory = fresh.ServiceProvider.GetRequiredService<IUserRoleDirectory>();
        (await directory.MayStartAccessAsync(id)).Should().BeFalse();
        (await directory.MayStartAccessAsync(Guid.NewGuid())).Should().BeFalse("an account the store does not know may not start access");
    }

    private static async Task<List<ModulusAccountTransition>> Transitions(ServiceProvider provider, Guid userId)
    {
        using var scope = provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ModulusIdentityDbContext>().AccountTransitions.AsNoTracking()
            .Where(t => t.UserId == userId).OrderBy(t => t.Id).ToListAsync();
    }

    private static async Task<(ServiceProvider Provider, SqliteConnection Connection)> CreateAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddSingleton<Microsoft.Extensions.Hosting.IHostEnvironment>(new FakeEnvironment());
        services.AddSingleton<IIdentityEmailSender>(new SilentSender());
        services.AddScoped<ICurrentTenant, Modulus.Core.Null.NullCurrentTenant>();
        services.AddDbContext<ModulusIdentityDbContext>(o => o.UseSqlite(connection));
        services.AddModulusIdentity<ModulusIdentityDbContext, ModulusUser, ModulusRole>(new ConfigurationBuilder().Build());
        var provider = services.BuildServiceProvider();
        using (var scope = provider.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ModulusIdentityDbContext>().Database.EnsureCreatedAsync();
        return (provider, connection);
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
