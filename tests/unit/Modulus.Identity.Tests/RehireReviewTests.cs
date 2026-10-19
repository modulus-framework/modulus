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

/// <summary>A reviewed rehire: only an approver in the account's company may bring an archived account back, and only by approval.</summary>
[Trait("Category", "Unit")]
public sealed class RehireReviewTests
{
    [Fact]
    public async Task A_tenant_administrator_approves_an_archived_account_into_pending_activation()
    {
        var (provider, audit, connection) = await CreateAsync();
        await using var _ = provider;
        await using var __ = connection;
        var account = await ArchivedAccountAsync(provider);
        var approver = await AccountWithRoleAsync(provider, "TenantAdministrator");

        var state = await Approve(provider, account, approver);

        state.Should().Be(AccountLifecycle.PendingActivation);
        (await Stored(provider, account)).Lifecycle.Should().Be(AccountLifecycle.PendingActivation);
        audit.Events.Should().ContainSingle(e => e.Action == "account.rehire_approved" && e.Actor == approver.ToString());
    }

    [Fact]
    public async Task An_hr_approver_is_accepted_too()
    {
        var (provider, _, connection) = await CreateAsync();
        await using var _ = provider;
        await using var __ = connection;
        var account = await ArchivedAccountAsync(provider);
        var approver = await AccountWithRoleAsync(provider, "HR");

        (await Approve(provider, account, approver)).Should().Be(AccountLifecycle.PendingActivation);
    }

    [Fact]
    public async Task An_account_without_an_approver_role_cannot_approve()
    {
        var (provider, _, connection) = await CreateAsync();
        await using var _ = provider;
        await using var __ = connection;
        var account = await ArchivedAccountAsync(provider);
        var approver = await AccountWithRoleAsync(provider, "Buyer");

        await FluentActions.Invoking(() => Approve(provider, account, approver)).Should().ThrowAsync<ForbiddenException>();
        (await Stored(provider, account)).Lifecycle.Should().Be(AccountLifecycle.Archived, "nothing changed");
    }

    [Fact]
    public async Task An_account_cannot_approve_its_own_rehire()
    {
        var (provider, _, connection) = await CreateAsync();
        await using var _ = provider;
        await using var __ = connection;
        var account = await ArchivedAccountAsync(provider);
        await AddRoleAsync(provider, account, "TenantAdministrator");

        await FluentActions.Invoking(() => Approve(provider, account, account)).Should().ThrowAsync<ForbiddenException>();
        (await Stored(provider, account)).Lifecycle.Should().Be(AccountLifecycle.Archived);
    }

    [Fact]
    public async Task Only_an_archived_account_is_rehired()
    {
        var (provider, _, connection) = await CreateAsync();
        await using var _ = provider;
        await using var __ = connection;
        var account = await NewAccountAsync(provider);
        var approver = await AccountWithRoleAsync(provider, "TenantAdministrator");

        await FluentActions.Invoking(() => Approve(provider, account, approver)).Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task An_approver_from_another_company_cannot_approve()
    {
        var (provider, _, connection) = await CreateAsync();
        await using var _ = provider;
        await using var __ = connection;
        var account = await ArchivedAccountAsync(provider, tenantId: Guid.NewGuid());
        var approver = await AccountWithRoleAsync(provider, "TenantAdministrator", tenantId: Guid.NewGuid());

        await FluentActions.Invoking(() => Approve(provider, account, approver)).Should().ThrowAsync<ForbiddenException>();
        (await Stored(provider, account)).Lifecycle.Should().Be(AccountLifecycle.Archived);
    }

    private static async Task<AccountLifecycle> Approve(ServiceProvider provider, Guid account, Guid approver)
    {
        using var scope = provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IRehireReviewService>()
            .ApproveAsync(account, approver, "Returning for the Q4 contract");
    }

    private static async Task<Guid> ArchivedAccountAsync(ServiceProvider provider, Guid? tenantId = null)
    {
        var account = await NewAccountAsync(provider, tenantId);
        await Change(provider, new AccountLifecycleChange(account, AccountLifecycle.Disabled, null, SuspensionReasons.Other, "left"));
        await Change(provider, new AccountLifecycleChange(account, AccountLifecycle.Archived, null));
        return account;
    }

    private static async Task<Guid> AccountWithRoleAsync(ServiceProvider provider, string role, Guid? tenantId = null)
    {
        var account = await NewAccountAsync(provider, tenantId);
        await AddRoleAsync(provider, account, role);
        return account;
    }

    private static async Task AddRoleAsync(ServiceProvider provider, Guid account, string role)
    {
        using var scope = provider.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<ModulusRole>>();
        if (!await roles.RoleExistsAsync(role))
            (await roles.CreateAsync(new ModulusRole { Name = role })).Succeeded.Should().BeTrue();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ModulusUser>>();
        var user = (await users.FindByIdAsync(account.ToString("D")))!;
        (await users.AddToRoleAsync(user, role)).Succeeded.Should().BeTrue();
    }

    private static async Task<AccountLifecycle> Change(ServiceProvider provider, AccountLifecycleChange change)
    {
        using var scope = provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IAccountLifecycleService>().ChangeAsync(change);
    }

    private static async Task<Guid> NewAccountAsync(ServiceProvider provider, Guid? tenantId = null)
    {
        using var scope = provider.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ModulusUser>>();
        var user = new ModulusUser
        {
            UserName = $"{Guid.NewGuid():N}@example.test",
            Email = $"{Guid.NewGuid():N}@example.test",
            TenantId = tenantId,
        };
        (await users.CreateAsync(user)).Succeeded.Should().BeTrue();
        return user.Id;
    }

    private static async Task<ModulusUser> Stored(ServiceProvider provider, Guid userId)
    {
        using var scope = provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ModulusIdentityDbContext>().Users.AsNoTracking()
            .SingleAsync(u => u.Id == userId);
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
