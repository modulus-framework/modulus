using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Exceptions;
using Modulus.Core.Abstractions.Security;
using Modulus.Identity.Abstractions;
using Modulus.Identity.EntityFrameworkCore;
using Modulus.Identity.Extensions;
using Xunit;

namespace Modulus.Identity.Tests;

/// <summary>
/// The explicit lifecycle API: the actor and reason are the ones passed in, suspension fields are required and cleared correctly,
/// illegal moves are refused without changing anything, and every change is audited.
/// </summary>
[Trait("Category", "Unit")]
public sealed class AccountLifecycleServiceTests
{
    private static readonly Guid Admin = Guid.Parse("0a0a0a0a-0000-4000-8000-000000000001");

    [Fact]
    public async Task Disabling_records_the_explicit_actor_reason_and_audits_the_change()
    {
        var (provider, audit, connection) = await CreateAsync();
        await using var _ = provider;
        await using var __ = connection;
        var userId = await NewAccountAsync(provider);

        using (var scope = provider.CreateScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<IAccountLifecycleService>()
                .ChangeAsync(new AccountLifecycleChange(userId, AccountLifecycle.Disabled, Admin, SuspensionReasons.SecurityReview, "Ticket 42"));
            result.Should().Be(AccountLifecycle.Disabled);
        }

        var stored = await Stored(provider, userId);
        stored.Lifecycle.Should().Be(AccountLifecycle.Disabled);
        stored.IsActive.Should().BeFalse();
        stored.SuspensionReason.Should().Be(SuspensionReasons.SecurityReview);
        stored.SuspensionNote.Should().Be("Ticket 42");
        var transition = (await Transitions(provider, userId)).Should().ContainSingle().Which;
        transition.ActorId.Should().Be(Admin);
        transition.Reason.Should().Be(SuspensionReasons.SecurityReview);
        audit.Events.Should().ContainSingle(e => e.Action == "account.lifecycle_changed" && e.Actor == Admin.ToString()
            && e.Details["to"] == "Disabled");
    }

    [Fact]
    public async Task Disabling_without_a_reason_is_refused()
    {
        var (provider, _, connection) = await CreateAsync();
        await using var _ = provider;
        await using var __ = connection;
        var userId = await NewAccountAsync(provider);

        var act = () => Change(provider, new AccountLifecycleChange(userId, AccountLifecycle.Disabled, Admin));

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*suspension reason*");
        (await Stored(provider, userId)).Lifecycle.Should().Be(AccountLifecycle.Active);
    }

    [Fact]
    public async Task A_reason_that_needs_a_note_is_refused_without_one()
    {
        var (provider, _, connection) = await CreateAsync();
        await using var _ = provider;
        await using var __ = connection;
        var userId = await NewAccountAsync(provider);

        var act = () => Change(provider, new AccountLifecycleChange(userId, AccountLifecycle.Disabled, Admin, SuspensionReasons.Other));

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*needs a note*");
    }

    [Fact]
    public async Task Reinstating_clears_the_suspension_and_records_the_move()
    {
        var (provider, _, connection) = await CreateAsync();
        await using var _ = provider;
        await using var __ = connection;
        var userId = await NewAccountAsync(provider);
        await Change(provider, new AccountLifecycleChange(userId, AccountLifecycle.Disabled, Admin, SuspensionReasons.Absence));

        await Change(provider, new AccountLifecycleChange(userId, AccountLifecycle.Active, Admin, "returned from leave"));

        var stored = await Stored(provider, userId);
        stored.Lifecycle.Should().Be(AccountLifecycle.Active);
        stored.IsActive.Should().BeTrue();
        stored.SuspensionReason.Should().BeNull();
        stored.SuspensionNote.Should().BeNull();
        (await Transitions(provider, userId)).Last().Reason.Should().Be("returned from leave");
    }

    [Fact]
    public async Task An_illegal_move_is_a_conflict_and_changes_nothing()
    {
        var (provider, _, connection) = await CreateAsync();
        await using var _ = provider;
        await using var __ = connection;
        var userId = await NewAccountAsync(provider);

        var act = () => Change(provider, new AccountLifecycleChange(userId, AccountLifecycle.Archived, Admin));

        await act.Should().ThrowAsync<ConflictException>().WithMessage("*Active*Archived*");
        (await Stored(provider, userId)).Lifecycle.Should().Be(AccountLifecycle.Active);
        (await Transitions(provider, userId)).Should().BeEmpty();
    }

    [Fact]
    public async Task An_archived_account_returns_only_through_a_reviewed_rehire()
    {
        var (provider, _, connection) = await CreateAsync();
        await using var _ = provider;
        await using var __ = connection;
        var userId = await NewAccountAsync(provider);
        await Change(provider, new AccountLifecycleChange(userId, AccountLifecycle.Disabled, Admin, SuspensionReasons.Other, "left the company"));
        await Change(provider, new AccountLifecycleChange(userId, AccountLifecycle.Archived, Admin));

        await FluentActions.Invoking(() => Change(provider, new AccountLifecycleChange(userId, AccountLifecycle.PendingActivation, Admin)))
            .Should().ThrowAsync<ConflictException>();

        await Change(provider, new AccountLifecycleChange(userId, AccountLifecycle.PendingActivation, Admin, "rehire review 7", RehireReviewed: true));
        (await Stored(provider, userId)).Lifecycle.Should().Be(AccountLifecycle.PendingActivation);
    }

    [Fact]
    public async Task Changing_to_the_current_state_is_a_no_op()
    {
        var (provider, _, connection) = await CreateAsync();
        await using var _ = provider;
        await using var __ = connection;
        var userId = await NewAccountAsync(provider);

        (await Change(provider, new AccountLifecycleChange(userId, AccountLifecycle.Active, Admin))).Should().Be(AccountLifecycle.Active);

        (await Transitions(provider, userId)).Should().BeEmpty();
    }

    [Fact]
    public async Task An_unknown_account_is_not_found()
    {
        var (provider, _, connection) = await CreateAsync();
        await using var _ = provider;
        await using var __ = connection;

        await FluentActions.Invoking(() => Change(provider, new AccountLifecycleChange(Guid.NewGuid(), AccountLifecycle.Disabled, Admin, "other")))
            .Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task A_system_change_records_no_actor()
    {
        var (provider, _, connection) = await CreateAsync();
        await using var _ = provider;
        await using var __ = connection;
        var userId = await NewAccountAsync(provider);

        await Change(provider, new AccountLifecycleChange(userId, AccountLifecycle.Disabled, null, SuspensionReasons.PolicyViolation));

        (await Transitions(provider, userId)).Should().ContainSingle().Which.ActorId.Should().BeNull();
    }

    private static async Task<AccountLifecycle> Change(ServiceProvider provider, AccountLifecycleChange change)
    {
        using var scope = provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IAccountLifecycleService>().ChangeAsync(change);
    }

    private static async Task<Guid> NewAccountAsync(ServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ModulusUser>>();
        var user = new ModulusUser { UserName = $"{Guid.NewGuid():N}@example.test", Email = $"{Guid.NewGuid():N}@example.test" };
        (await users.CreateAsync(user)).Succeeded.Should().BeTrue();
        return user.Id;
    }

    private static async Task<ModulusUser> Stored(ServiceProvider provider, Guid userId)
    {
        using var scope = provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ModulusIdentityDbContext>().Users.AsNoTracking()
            .SingleAsync(u => u.Id == userId);
    }

    private static async Task<List<ModulusAccountTransition>> Transitions(ServiceProvider provider, Guid userId)
    {
        using var scope = provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ModulusIdentityDbContext>().AccountTransitions.AsNoTracking()
            .Where(t => t.UserId == userId).OrderBy(t => t.Id).ToListAsync();
    }

    private static async Task<(ServiceProvider Provider, RecordingAudit Audit, SqliteConnection Connection)> CreateAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var audit = new RecordingAudit();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddSingleton<Microsoft.Extensions.Hosting.IHostEnvironment>(new FakeEnvironment());
        services.AddSingleton<IIdentityEmailSender>(new SilentSender());
        services.AddSingleton<ISecurityAuditLog>(audit);
        services.AddScoped<ICurrentTenant, Modulus.Core.Null.NullCurrentTenant>();
        services.AddDbContext<ModulusIdentityDbContext>(o => o.UseSqlite(connection));
        services.AddModulusIdentity<ModulusIdentityDbContext, ModulusUser, ModulusRole>(new ConfigurationBuilder().Build());
        var provider = services.BuildServiceProvider();
        using (var scope = provider.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ModulusIdentityDbContext>().Database.EnsureCreatedAsync();
        return (provider, audit, connection);
    }

    private sealed class RecordingAudit : ISecurityAuditLog
    {
        public List<SecurityAuditEvent> Events { get; } = [];

        public void Record(SecurityAuditEvent auditEvent) => Events.Add(auditEvent);
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
