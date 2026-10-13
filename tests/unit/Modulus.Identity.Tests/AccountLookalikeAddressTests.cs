namespace Modulus.Identity.Tests;

using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
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
/// S1: an address the caller typed can differ from the stored one yet resolve to the same account through the normalized
/// lookup. The reset token must go only to the address stored on that account, never to the address the caller typed.
/// The dotless-ı lookalike from the review does not match on this runtime's normalizer, so this test uses a case variant,
/// which exercises the same path.
/// </summary>
[Trait("Category", "Unit")]
public sealed class AccountLookalikeAddressTests : IAsyncLifetime
{
    private const string VictimEmail = "admin@github.com";
    private const string LookalikeEmail = "ADMIN@GITHUB.COM";

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

    [Fact]
    public async Task Reset_mail_for_a_differently_typed_address_goes_to_the_stored_address_only()
    {
        using var host = _provider.GetRequiredService<ICurrentTenant>().Change(null);
        await using var scope = _provider.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ModulusUser>>();
        (await users.CreateAsync(new ModulusUser { UserName = "admin", Email = VictimEmail, IsActive = true }, "Passw0rd!x"))
            .Succeeded.Should().BeTrue();
        (await users.FindByEmailAsync(LookalikeEmail)).Should().NotBeNull("the typed address must reach the victim's account for this test to mean anything");

        // Only the lookup and the queue are used on this path; the other collaborators are not touched.
        var controller = new AccountController<ModulusUser>(users, null!, _queue, null!, null!, null!);

        var result = await controller.ForgotPasswordAsync(LookalikeEmail);

        result.Should().BeOfType<OkObjectResult>();
        (await WaitForAsync(VictimEmail)).Should().NotBeNullOrWhiteSpace();
        _sender.Recipients.Should().BeEquivalentTo([VictimEmail]);
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

    private sealed class RecordingSender : IIdentityEmailSender
    {
        public ConcurrentDictionary<string, string> Sent { get; } = new();

        public ConcurrentBag<string> Recipients { get; } = new();

        public Task SendPasswordResetEmailAsync(string email, string resetToken, CancellationToken ct = default)
        {
            Recipients.Add(email);
            Sent[email] = resetToken;
            return Task.CompletedTask;
        }

        public Task SendEmailConfirmationEmailAsync(string email, string confirmationToken, CancellationToken ct = default)
        {
            Recipients.Add(email);
            Sent[email] = confirmationToken;
            return Task.CompletedTask;
        }
    }
}
