namespace Modulus.Authorization.EntityFrameworkCore;

/// <summary>Settings for the persistent data-quality finding sink (<c>Authorization:DataQuality</c>).</summary>
public sealed class DataQualityFindingOptions
{
    /// <summary>How long a finding is kept before the purge removes it. Default 90 days (owner decision, plan section 13).</summary>
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(90);

    /// <summary>How often the purge runs. Default hourly.</summary>
    public TimeSpan PurgeInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Findings held in memory while they wait to be written. A full queue drops new findings and counts them.</summary>
    public int Capacity { get; set; } = 10_000;

    /// <summary>The most findings written in one batch.</summary>
    public int BatchSize { get; set; } = 200;
}
