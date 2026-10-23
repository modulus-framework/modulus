namespace Modulus.BackgroundJobs;

/// <summary>
/// The running job's context (A6-2), scoped to one job execution. The worker sets <see cref="Actor"/> before the job starts, so a job can
/// tell who it runs as (see <see cref="JobActor"/>). It is null outside a job.
/// </summary>
public sealed class JobContext
{
    /// <summary>The actor the current job runs as, such as <c>system:nightly-export</c> or <c>person:{id}</c>.</summary>
    public string? Actor { get; set; }

    /// <summary>The agent role that acted for the person who queued the running job (A9-1); null when no agent did.</summary>
    public string? AgentId { get; set; }

    /// <summary>
    /// The roles the person who queued the running job holds, read from the identity store when the job starts (never stored with the
    /// queued job, so a role removed after queueing is not used). Empty for a system job, an unknown person, or when no directory is registered
    /// (so role grants are refused).
    /// </summary>
    public IReadOnlyCollection<string> Roles { get; internal set; } = [];

    /// <summary>The person who queued the running job, read from <see cref="Actor"/>; null for a system job or outside a job.</summary>
    public Guid? InitiatorId
        => Actor is { } actor
           && actor.StartsWith(JobActor.PersonPrefix, StringComparison.Ordinal)
           && Guid.TryParseExact(actor[JobActor.PersonPrefix.Length..], "N", out var id)
            ? id
            : null;
}
