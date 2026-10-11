namespace Modulus.Identity;

using System.Diagnostics.Metrics;
using System.Threading.Channels;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Modulus.Core.Abstractions;
using Modulus.Identity.Abstractions;

/// <summary>
/// Runs the "mint a token and e-mail it" half of the anonymous account flows off the request. Doing it inline made an
/// existing account measurably slower to answer than an unknown one (a token and a mail send versus nothing), which is an
/// account-enumeration oracle even when the response body is identical.
/// </summary>
/// <remarks>
/// The queue carries the account id only. The worker mails the address stored on the account, never an address the caller
/// typed: a lookalike address that normalizes to a victim's account would otherwise receive the reset token.
/// </remarks>
public interface IIdentityEmailQueue
{
    /// <summary>Queues a password-reset mail for the stored address of <paramref name="userId"/>; false when the queue is full (the request still answers uniformly).</summary>
    bool EnqueuePasswordReset(Guid userId);

    /// <summary>Queues an e-mail-confirmation mail for the stored address of <paramref name="userId"/>; false when the queue is full.</summary>
    bool EnqueueEmailConfirmation(Guid userId);
}

/// <summary>Bounded in-process queue plus its worker; each item runs in its own scope inside the host tenant context.</summary>
internal sealed class IdentityEmailQueue<TUser>(IServiceScopeFactory scopes, ILogger<IdentityEmailQueue<TUser>> logger)
    : BackgroundService, IIdentityEmailQueue
    where TUser : ModulusUser, new()
{
    private static readonly Meter s_meter = new("Modulus.Identity");
    private static readonly Counter<long> s_dropped = s_meter.CreateCounter<long>("modulus.identity.email.dropped");

    // Bounded, so an anonymous caller flooding the endpoint cannot grow memory without limit.
    private readonly Channel<(bool Reset, Guid UserId)> _queue =
        Channel.CreateBounded<(bool, Guid)>(new BoundedChannelOptions(1_000)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
        });

    public bool EnqueuePasswordReset(Guid userId) => Enqueue(reset: true, userId);

    public bool EnqueueEmailConfirmation(Guid userId) => Enqueue(reset: false, userId);

    private bool Enqueue(bool reset, Guid userId)
    {
        if (_queue.Writer.TryWrite((reset, userId)))
            return true;

        // A dropped mail is a user who never gets one; make that visible without logging anything about the account.
        s_dropped.Add(1);
        logger.LogWarning("Account e-mail queue is full; a password-reset or confirmation mail was dropped.");
        return false;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var (reset, userId) in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var services = scope.ServiceProvider;

                // The user store is host-filtered, and a background scope has no company: enter the host like the controller does.
                using var host = services.GetRequiredService<ICurrentTenant>().Change(null);

                var users = services.GetRequiredService<UserManager<TUser>>();
                var user = await users.FindByIdAsync(userId.ToString());
                if (user is null || !user.IsActive || string.IsNullOrWhiteSpace(user.Email))
                    continue;

                // The stored address is the only destination: the token goes where the account's owner registered.
                var email = user.Email;
                var sender = services.GetRequiredService<IIdentityEmailSender>();
                if (reset)
                {
                    await sender.SendPasswordResetEmailAsync(email, await users.GeneratePasswordResetTokenAsync(user), stoppingToken);
                }
                else if (!await users.IsEmailConfirmedAsync(user))
                {
                    await sender.SendEmailConfirmationEmailAsync(email, await users.GenerateEmailConfirmationTokenAsync(user), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Never surface to the (already answered) caller and never log the address.
                logger.LogError(ex, "Sending an account e-mail failed.");
            }
        }
    }
}
