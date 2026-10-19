namespace Modulus.BackgroundJobs;

/// <summary>
/// The running job's context (A6-2), scoped to one job execution. The worker sets <see cref="Actor"/> before the job starts, so a job can
/// tell who it runs as (see <see cref="JobActor"/>). It is null outside a job.
/// </summary>
public sealed class JobContext
{
    /// <summary>The actor the current job runs as, such as <c>system:nightly-export</c> or <c>person:{id}</c>.</summary>
    public string? Actor { get; set; }
}
