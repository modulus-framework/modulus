namespace Modulus.BackgroundJobs;

/// <summary>
/// Decides, when a queued job comes up to run, whether the person who queued it may still have it run (A6-1). A job queued by a
/// signed-in person carries that person's id; a job queued without one (recurring jobs, or work queued by the system) is not checked.
/// A refused job is skipped and logged, not retried. Identity registers a guard that refuses an account that may no longer start access
/// (disabled, suspended, archived). The default allows every job, so an application without an identity provider behaves as before.
/// </summary>
public interface IJobInitiatorGuard
{
    /// <summary>Whether the job queued by <paramref name="initiatorId"/> may run now.</summary>
    Task<bool> MayRunAsync(Guid initiatorId, CancellationToken ct);

    /// <summary>
    /// Whether the job queued by <paramref name="initiatorId"/> may perform a step that needs <paramref name="permission"/> (the
    /// per-stage re-check, <see cref="IJobStageGuard"/>). The default answers only the start check, so an application without an
    /// identity provider behaves as before; Identity answers the permission too.
    /// </summary>
    Task<bool> MayPerformAsync(Guid initiatorId, string permission, CancellationToken ct) => MayRunAsync(initiatorId, ct);

    /// <summary>
    /// Whether the job queued by <paramref name="initiatorId"/> through the agent <paramref name="agentRole"/> may perform a step that
    /// needs <paramref name="permission"/>: the person's permission, capped by the agent's (A9-1). The default ignores the agent.
    /// </summary>
    Task<bool> MayPerformAsAgentAsync(Guid initiatorId, string agentRole, string permission, CancellationToken ct)
        => MayPerformAsync(initiatorId, permission, ct);
}

internal sealed class AllowAllJobInitiators : IJobInitiatorGuard
{
    public Task<bool> MayRunAsync(Guid initiatorId, CancellationToken ct) => Task.FromResult(true);
}
