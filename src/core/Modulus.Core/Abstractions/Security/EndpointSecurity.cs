namespace Modulus.Core.Abstractions.Security;

/// <summary>
/// Records <b>why</b> an endpoint is open to anonymous callers. Every endpoint is closed by default
/// (the fallback policy requires a signed-in user); opening one is a reviewed decision, so the reason
/// (and optionally a ticket) travels with the endpoint and the startup security guard reports it and
/// checks it against the loosening allow-list. Used as endpoint metadata (<c>.Loosen(reason)</c> on a
/// minimal API, <c>AllowAnonymous(reason)</c> in a REPR endpoint) or as an attribute next to
/// <c>[AllowAnonymous]</c> on a controller action or Razor page. The attribute alone does not open
/// anything: <c>[AllowAnonymous]</c> (or <c>.AllowAnonymous()</c>) does.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class LoosenedAttribute : Attribute
{
    /// <summary>Records <paramref name="reason"/> as the loosening reason.</summary>
    /// <param name="reason">Why anonymous access is needed (shown in the loosening report).</param>
    public LoosenedAttribute(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Reason = reason;
    }

    /// <summary>Why anonymous access is needed.</summary>
    public string Reason { get; }

    /// <summary>The review or change ticket that approved the loosening, if any.</summary>
    public string? Ticket { get; set; }

    /// <summary>
    /// Set by Modulus packages on the endpoints they open (health probes, sign-in, OpenID Connect): the
    /// reason was reviewed in the framework, so the startup guard reports the endpoint but does not
    /// require an allow-list entry. The allow-list governs the app's own loosenings; app code leaves this
    /// <see langword="false"/>.
    /// </summary>
    public bool Framework { get; set; }
}

/// <summary>How sensitive the data an endpoint returns or changes is.</summary>
public enum DataClassification
{
    /// <summary>Not declared.</summary>
    Unspecified = 0,

    /// <summary>Meant for anyone.</summary>
    Public,

    /// <summary>Any signed-in user of the company may see it.</summary>
    Internal,

    /// <summary>Restricted to the users the permission model allows. Never anonymous.</summary>
    Confidential,

    /// <summary>The most sensitive data (payroll, health, secrets). Never anonymous.</summary>
    Restricted,
}

/// <summary>
/// Where a caller must connect from. Reserved for network trust (ERP guideline B4): until network
/// context is enforced, the startup security guard refuses any value other than <see cref="Any"/>,
/// so a declared restriction can never silently go unenforced.
/// </summary>
public enum NetworkRequirement
{
    /// <summary>No network restriction.</summary>
    Any = 0,

    /// <summary>Only from a company network.</summary>
    CompanyNetwork,

    /// <summary>Only from the selected branch's network.</summary>
    BranchNetwork,

    /// <summary>Only from inside the deployment (service to service).</summary>
    Internal,
}

/// <summary>
/// Security facts about an endpoint beyond who may call it: its data classification and its network
/// requirement. Endpoint metadata or an attribute; read by the startup security guard.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class EndpointSecurityPolicyAttribute : Attribute
{
    /// <summary>The data classification.</summary>
    public DataClassification Classification { get; set; }

    /// <summary>The network requirement (only <see cref="NetworkRequirement.Any"/> is enforceable today).</summary>
    public NetworkRequirement Network { get; set; }
}
