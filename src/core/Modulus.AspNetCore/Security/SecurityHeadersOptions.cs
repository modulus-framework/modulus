namespace Modulus.AspNetCore.Security;

/// <summary>
/// Controls the response headers written by <see cref="SecurityHeadersExtensions.UseModulusSecurityHeaders"/>.
/// Binds from the <c>SecurityHeaders</c> configuration section.
/// </summary>
public sealed class SecurityHeadersOptions
{
    public const string SectionName = "SecurityHeaders";

    /// <summary><c>X-Content-Type-Options: nosniff</c>. Prevents MIME sniffing.</summary>
    public bool ContentTypeOptions { get; set; } = true;

    /// <summary><c>X-Frame-Options</c> value (e.g. <c>DENY</c>, <c>SAMEORIGIN</c>). Empty ⇒ omit.</summary>
    public string FrameOptions { get; set; } = "DENY";

    /// <summary><c>Referrer-Policy</c> value. Empty ⇒ omit.</summary>
    public string ReferrerPolicy { get; set; } = "strict-origin-when-cross-origin";

    /// <summary><c>Content-Security-Policy</c> value. Empty ⇒ omit (recommended to set for browser-facing apps).</summary>
    public string ContentSecurityPolicy { get; set; } = string.Empty;

    /// <summary><c>Permissions-Policy</c> value. Empty ⇒ omit.</summary>
    public string PermissionsPolicy { get; set; } = string.Empty;

    /// <summary>Emit <c>Strict-Transport-Security</c>. Only sent over HTTPS.</summary>
    public bool EnableHsts { get; set; } = true;

    /// <summary>HSTS <c>max-age</c>, in seconds. Default 1 year.</summary>
    public int HstsMaxAgeSeconds { get; set; } = 31_536_000;

    /// <summary>Add <c>includeSubDomains</c> to the HSTS header.</summary>
    public bool HstsIncludeSubDomains { get; set; } = true;

    /// <summary>Remove the <c>Server</c> response header.</summary>
    public bool RemoveServerHeader { get; set; } = true;
}
