namespace Modulus.BackgroundJobs;

/// <summary>
/// Who a background job runs as (A6-2). The actor is a name carried with the job, not a stored principal: a person who queued the job is
/// <c>person:{id}</c>, and a job that runs as the system is <c>system:{name}</c>, where the name is the recurring job id or, for an ad
/// hoc job, the job type. Logs and the running job see the same name.
/// </summary>
public static class JobActor
{
    /// <summary>Prefix of an actor that is a signed-in person.</summary>
    public const string PersonPrefix = "person:";

    /// <summary>Prefix of an actor that is the system, with no person behind it.</summary>
    public const string SystemPrefix = "system:";

    /// <summary>The actor for a job queued by the person <paramref name="userId"/>.</summary>
    public static string ForPerson(Guid userId) => PersonPrefix + userId.ToString("N");

    /// <summary>The actor for a job that runs as the system, named <paramref name="name"/>.</summary>
    public static string ForSystem(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return SystemPrefix + name;
    }
}
