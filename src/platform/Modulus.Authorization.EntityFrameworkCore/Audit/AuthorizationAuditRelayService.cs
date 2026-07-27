namespace Modulus.Authorization.EntityFrameworkCore.Audit;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Polling loop around <see cref="AuthorizationAuditRelayProcessor"/> — see its
/// remarks for what actually happens each interval.
/// </summary>
public sealed class AuthorizationAuditRelayService(
    IServiceProvider sp,
    IOptions<AuthorizationAuditOptions> opts,
    ILogger<AuthorizationAuditRelayService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(opts.Value.PollingIntervalSec);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Resolve the processor from a fresh scope every iteration —
                // this BackgroundService is effectively singleton, and
                // constructor-injecting a scoped processor would capture a
                // single scope for the process lifetime (captive dependency).
                await using var scope = sp.CreateAsyncScope();
                var processor = scope.ServiceProvider
                    .GetRequiredService<AuthorizationAuditRelayProcessor>();
                await processor.ProcessAsync(stoppingToken);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                // A transient DB failure or missing IOutboxDispatcher must NOT
                // terminate the polling loop — log and retry next interval.
                logger.LogError(ex,
                    "Authorization audit relay iteration failed; will retry on next interval.");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }
}
