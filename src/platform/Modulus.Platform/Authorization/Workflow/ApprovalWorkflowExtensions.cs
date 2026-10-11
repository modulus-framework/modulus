namespace Modulus.Authorization.Workflow;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>Registers the multi-step approval workflow.</summary>
public static class ApprovalWorkflowExtensions
{
    /// <summary>
    /// Adds <see cref="IApprovalWorkflow"/> with in-memory chain and request stores (replaced by the EF stores of
    /// <c>AddEfCoreAuthorizationStores</c>) and a background service that escalates overdue steps. Needs
    /// <c>AddModulusAuthorization()</c> (approval authority, current principal).
    /// </summary>
    public static IServiceCollection AddApprovalWorkflow(this IServiceCollection services, Action<ApprovalWorkflowOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = services.AddOptions<ApprovalWorkflowOptions>();
        if (configure is not null)
            options.Configure(configure);

        services.TryAddSingleton<IApprovalChainStore, InMemoryApprovalChainStore>();
        services.TryAddSingleton<IApprovalRequestStore, InMemoryApprovalRequestStore>();
        services.TryAddScoped<IApprovalWorkflow, ApprovalWorkflow>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, ApprovalEscalationService>());
        return services;
    }
}

/// <summary>Looks for approval steps that waited longer than their limit and escalates them.</summary>
internal sealed class ApprovalEscalationService(
    IServiceScopeFactory scopes,
    IOptions<ApprovalWorkflowOptions> options,
    ILogger<ApprovalEscalationService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.EnableEscalation)
            return;

        using var timer = new PeriodicTimer(options.Value.EscalationCheckInterval);
        while (await WaitAsync(timer, stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var count = await scope.ServiceProvider.GetRequiredService<IApprovalWorkflow>().EscalateOverdueAsync(stoppingToken);
                if (count > 0)
                    logger.LogInformation("Escalated {Count} overdue approval step(s).", count);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Looking for overdue approvals failed; trying again at the next interval.");
            }
        }
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
