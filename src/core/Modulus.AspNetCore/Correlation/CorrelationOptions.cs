namespace Modulus.AspNetCore.Correlation;

using Modulus.Core.Abstractions;

/// <summary>
/// Options for <c>AddModulusCorrelation</c> / <c>UseModulusCorrelation</c>,
/// bound from the <c>Correlation</c> configuration section.
/// </summary>
public sealed class CorrelationOptions
{
    public const string SectionName = "Correlation";

    /// <summary>Inbound/outbound header carrying the id. Default <c>X-Correlation-ID</c>.</summary>
    public string HeaderName { get; set; } = CorrelationHeaders.Default;

    /// <summary>Echo the resolved id back on the response. Default <see langword="true"/>.</summary>
    public bool IncludeInResponse { get; set; } = true;

    /// <summary>
    /// When an inbound request carries no id, derive one from the current trace
    /// id (<see cref="System.Diagnostics.Activity"/>) instead of a fresh GUID, so
    /// the correlation id and trace id match. Falls back to a GUID when no
    /// activity is present. Default <see langword="true"/>.
    /// </summary>
    public bool UseTraceIdWhenMissing { get; set; } = true;
}
