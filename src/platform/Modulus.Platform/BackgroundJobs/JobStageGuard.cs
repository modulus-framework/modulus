using Modulus.Core.Abstractions.Exceptions;

namespace Modulus.BackgroundJobs;

/// <summary>
/// Re-checks, inside a running job, that the person who queued it still holds the permission a sensitive step needs (A6-1). A
/// job with no person behind it (a system job, or none) is not checked, as before. Runs in the job's own scope, so it reads the
/// <see cref="JobContext"/> the worker set for that job.
/// </summary>
public interface IJobStageGuard
{
    /// <summary>Throws <see cref="ForbiddenException"/> when the initiator of the running job no longer holds <paramref name="permission"/>.</summary>
    Task EnsureAllowedAsync(string permission, CancellationToken ct = default);

    /// <summary>
    /// Whether the person who queued the running job still holds <paramref name="permission"/>. A job with no person behind it is
    /// not checked, so this answers true for it, as <see cref="EnsureAllowedAsync"/> does.
    /// </summary>
    Task<bool> HoldsAsync(string permission, CancellationToken ct = default);
}

internal sealed class JobStageGuard(JobContext context, IJobInitiatorGuard initiators) : IJobStageGuard
{
    public async Task EnsureAllowedAsync(string permission, CancellationToken ct = default)
    {
        if (!await HoldsAsync(permission, ct))
            throw new ForbiddenException(permission, "The person who queued this job no longer holds the permission this step needs.");
    }

    public async Task<bool> HoldsAsync(string permission, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);

        if (context.InitiatorId is not { } initiator)
            return true;
        return context.AgentId is { } agent
            ? await initiators.MayPerformAsAgentAsync(initiator, agent, permission, ct)
            : await initiators.MayPerformAsync(initiator, permission, ct);
    }
}
