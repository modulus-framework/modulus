namespace Modulus.Identity.Tests;

using System.Collections.Concurrent;
using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Identity.Abstractions;
using Xunit;

/// <summary>
/// S1: the reset and confirmation mails go to the address stored on the account, never to the address the caller typed.
/// A dotless-i address normalizes to the same key as the victim's address, so it finds the victim's account; the mail must
/// still go to the victim.
/// </summary>
[Trait("Category", "Unit")]
public sealed class AccountMailLookalikeTests : OidcHostTestBase
{
    private const string VictimEmail = "victims@example.test";

    // "victimſ" ends in U+017F (long s), which upper-cases to "S" and so matches the stored normalized address.
    private const string LookalikeEmail = "victimſ@example.test";

    private readonly ConcurrentQueue<(string Kind, string Email)> _sent = new();

    private sealed class RecordingSender(ConcurrentQueue<(string Kind, string Email)> sent) : IIdentityEmailSender
    {
        public Task SendPasswordResetEmailAsync(string email, string resetToken, CancellationToken ct = default)
        {
            sent.Enqueue(("reset", email));
            return Task.CompletedTask;
        }

        public Task SendEmailConfirmationEmailAsync(string email, string confirmationToken, CancellationToken ct = default)
        {
            sent.Enqueue(("confirm", email));
            return Task.CompletedTask;
        }
    }

    protected override void ConfigureServices(IServiceCollection services)
        => services.AddSingleton<IIdentityEmailSender>(new RecordingSender(_sent));

    [Fact]
    public async Task A_password_reset_for_a_lookalike_address_is_mailed_to_the_stored_address_only()
    {
        await CreateUserAsync("victims");

        using (var client = NewClient())
        {
            var response = await client.PostAsync($"/account/forgot-password?email={Uri.EscapeDataString(LookalikeEmail)}", null);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        await WaitForMailAsync();
        _sent.Should().ContainSingle().Which.Should().Be(("reset", VictimEmail));
    }

    [Fact]
    public async Task A_confirmation_resend_for_a_lookalike_address_is_mailed_to_the_stored_address_only()
    {
        // Unconfirmed, so the resend is due.
        using (var scope = _app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ModulusUser>>();
            (await users.CreateAsync(new ModulusUser { UserName = "victims2", Email = VictimEmail }, Password))
                .Succeeded.Should().BeTrue();
        }

        using (var client = NewClient())
        {
            var response = await client.PostAsync($"/account/send-confirmation-email?email={Uri.EscapeDataString(LookalikeEmail)}", null);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        await WaitForMailAsync();
        _sent.Should().ContainSingle().Which.Should().Be(("confirm", VictimEmail));
    }

    private async Task WaitForMailAsync()
    {
        // Mail is sent off the request; give the queue a moment, then check nothing else arrives later.
        for (var i = 0; i < 100 && _sent.IsEmpty; i++)
            await Task.Delay(20);
        await Task.Delay(100);
    }
}
