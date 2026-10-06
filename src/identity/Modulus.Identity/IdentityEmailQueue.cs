namespace Modulus.Identity;

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
public interface IIdentityEmailQueue
{
    /// <summary>Queues a password-reset mail for <paramref name="userId"/>; false when the queue is full (the request still answers uniformly).</summary>
    bool EnqueuePasswordReset(Guid userId, string email);

    /// <summary>Queues an e-mail-confirmation mail for <paramref name="userId"/>.</summary>
    bool EnqueueEmailConfirmation(Guid userId, string email);
}

/// <summary>Bounded in-process queue plus its worker; each item runs in its own scope inside the host tenant context.</summary>
internal sealed class IdentityEmailQueue<TUser>(IServiceScopeFactory scopes, ILogger<IdentityEmailQueue<TUser>> logger)
    : BackgroundService, IIdentityEmailQueue
    where TUser : ModulusUser, new()
{
    // Bounded, so an anonymous caller flooding the endpoint cannot grow memory without limit.
    private readonly Channel<(bool Reset, Guid UserId, string Email)> _queue =
        Channel.CreateBounded<(bool, Guid, string)>(new BoundedChannelOptions(1_000)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
        });

    public bool EnqueuePasswordReset(Guid userId, string email) => _queue.Writer.TryWrite((true, userId, email));

    public bool EnqueueEmailConfirmation(Guid userId, string email) => _queue.Writer.TryWrite((false, userId, email));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var (reset, userId, email) in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var services = scope.ServiceProvider;

                // The user store is host-filtered, and a background scope has no company: enter the host like the controller does.
                using var host = services.GetRequiredService<ICurrentTenant>().Change(null);

                var users = services.GetRequiredService<UserManager<TUser>>();
                var user = await users.FindByIdAsync(userId.ToString());
                if (user is null || !user.IsActive)
                    continue;

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
