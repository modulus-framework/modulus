namespace Modulus.Identity.Tests;

using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Modulus.Core.Abstractions;
using Modulus.Identity.Abstractions;
using Modulus.Identity.EntityFrameworkCore;
using Modulus.MultiTenancy;
using Xunit;

/// <summary>
/// Reset and confirmation mails are produced off the request, so an existing account answers as fast as an unknown one.
/// The worker has no company of its own and the user store is host-filtered, so it must enter the host context itself.
/// </summary>
[Trait("Category", "Unit")]
public sealed class IdentityEmailQueueTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private ServiceProvider _provider = null!;
    private IdentityEmailQueue<ModulusUser> _queue = null!;
    private readonly RecordingSender _sender = new();

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddSingleton<ICurrentTenant>(new CurrentTenant());
        services.AddDbContext<ModulusIdentityDbContext>(o => o.UseSqlite(_connection));
        services.AddIdentityCore<ModulusUser>().AddEntityFrameworkStores<ModulusIdentityDbContext>().AddDefaultTokenProviders();
        services.AddScoped<IIdentityEmailSender>(_ => _sender);
        services.AddSingleton<IdentityEmailQueue<ModulusUser>>();
        _provider = services.BuildServiceProvider();

        // The test seeds as the host, exactly like the worker has to read.
        using (_provider.GetRequiredService<ICurrentTenant>().Change(null))
        {
            await using var scope = _provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ModulusIdentityDbContext>().Database.EnsureCreatedAsync();
        }

        _queue = _provider.GetRequiredService<IdentityEmailQueue<ModulusUser>>();
        await _queue.StartAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await _queue.StopAsync(CancellationToken.None);
        await _provider.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private async Task<ModulusUser> AddUserAsync(string name, bool active = true, Guid? tenantId = null)
    {
        using var host = _provider.GetRequiredService<ICurrentTenant>().Change(null);
        await using var scope = _provider.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ModulusUser>>();
        var user = new ModulusUser { UserName = name, Email = $"{name}@example.test", IsActive = active, TenantId = tenantId };
        (await users.CreateAsync(user, "Passw0rd!x")).Succeeded.Should().BeTrue();
        return user;
    }

    private async Task<string> WaitForAsync(string expectedEmail)
    {
        for (var i = 0; i < 100; i++)
        {
            if (_sender.Sent.TryGetValue(expectedEmail, out var token))
                return token;
            await Task.Delay(20);
        }

        throw new TimeoutException($"No mail was sent to {expectedEmail}.");
    }

    [Fact]
    public async Task A_password_reset_mail_is_sent_with_a_real_token_even_for_a_tenant_account()
    {
        var user = await AddUserAsync("alice", tenantId: Guid.NewGuid());

        _queue.EnqueuePasswordReset(user.Id, user.Email!).Should().BeTrue();

        (await WaitForAsync(user.Email!)).Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task No_mail_is_sent_for_an_inactive_account_or_an_unknown_id()
    {
        var inactive = await AddUserAsync("bob", active: false);
        var live = await AddUserAsync("carol");

        _queue.EnqueuePasswordReset(inactive.Id, inactive.Email!);
        _queue.EnqueuePasswordReset(Guid.NewGuid(), "ghost@example.test");
        _queue.EnqueuePasswordReset(live.Id, live.Email!);

        // Items run in order: once the last one has been mailed the earlier two have been decided.
        await WaitForAsync(live.Email!);
        _sender.Sent.Keys.Should().BeEquivalentTo([live.Email!]);
    }

    [Fact]
    public async Task A_confirmation_mail_is_not_sent_to_an_already_confirmed_address()
    {
        var confirmed = await AddUserAsync("dave");
        var pending = await AddUserAsync("erin");
        using (_provider.GetRequiredService<ICurrentTenant>().Change(null))
        {
            await using var scope = _provider.CreateAsyncScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ModulusUser>>();
            var user = (await users.FindByIdAsync(confirmed.Id.ToString()))!;
            (await users.ConfirmEmailAsync(user, await users.GenerateEmailConfirmationTokenAsync(user))).Succeeded.Should().BeTrue();
        }

        _queue.EnqueueEmailConfirmation(confirmed.Id, confirmed.Email!);
        _queue.EnqueueEmailConfirmation(pending.Id, pending.Email!);

        await WaitForAsync(pending.Email!);
        _sender.Sent.Keys.Should().BeEquivalentTo([pending.Email!]);
    }

    private sealed class RecordingSender : IIdentityEmailSender
    {
        public ConcurrentDictionary<string, string> Sent { get; } = new();

        public Task SendPasswordResetEmailAsync(string email, string resetToken, CancellationToken ct = default)
        {
            Sent[email] = resetToken;
            return Task.CompletedTask;
        }

        public Task SendEmailConfirmationEmailAsync(string email, string confirmationToken, CancellationToken ct = default)
        {
            Sent[email] = confirmationToken;
            return Task.CompletedTask;
        }
    }
}
