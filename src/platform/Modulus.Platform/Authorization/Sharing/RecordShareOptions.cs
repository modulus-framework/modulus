namespace Modulus.Authorization.Sharing;

/// <summary>Limits on record shares (A8-1).</summary>
public sealed class RecordShareOptions
{
    /// <summary>The longest a share may run. A share is temporary by design, so the cap is short by default.</summary>
    public TimeSpan MaxDuration { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    /// The one action a share grants on the record (A8-1). A share is read access: the default is <c>view</c>. A policy that names its read
    /// action differently sets it here. A share never grants any other action.
    /// </summary>
    public string SharedAction { get; set; } = "view";
}
