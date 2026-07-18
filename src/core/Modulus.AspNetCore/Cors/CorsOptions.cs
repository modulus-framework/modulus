namespace Modulus.AspNetCore.Cors;

/// <summary>
/// Binds from the <c>Cors</c> configuration section. Backs
/// <see cref="CorsExtensions.AddModulusCors"/>.
/// </summary>
public sealed class ModulusCorsOptions
{
    public const string SectionName = "Cors";

    /// <summary>Allowed origins. Use a single <c>"*"</c> entry to allow any origin
    /// (only permitted when <see cref="AllowCredentials"/> is false).</summary>
    public string[] AllowedOrigins { get; set; } = [];

    /// <summary>Allowed HTTP methods. Empty ⇒ any method.</summary>
    public string[] AllowedMethods { get; set; } = [];

    /// <summary>Allowed request headers. Empty ⇒ any header.</summary>
    public string[] AllowedHeaders { get; set; } = [];

    /// <summary>Response headers exposed to the browser.</summary>
    public string[] ExposedHeaders { get; set; } = [];

    /// <summary>Allow cookies / <c>Authorization</c> to be sent cross-origin.
    /// Cannot be combined with a wildcard origin.</summary>
    public bool AllowCredentials { get; set; }

    /// <summary>Preflight cache lifetime, in seconds. 0 ⇒ leave to the browser default.</summary>
    public int PreflightMaxAgeSeconds { get; set; }
}
