namespace Modulus.AspNetCore.Configuration;

/// <summary>
/// Configures <see cref="SecretsGuardExtensions.AddModulusSecretsGuard"/> — the
/// startup guard rail that flags secrets committed to <c>appsettings*.json</c>
/// instead of being sourced from environment variables, User Secrets, or a vault.
/// Binds from the <c>SecretsGuard</c> configuration section.
/// </summary>
public sealed class SecretsGuardOptions
{
    /// <summary>Configuration section this binds from.</summary>
    public const string SectionName = "SecretsGuard";

    /// <summary>Master switch. When <see langword="false"/> the guard does nothing.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Throw at startup on a violation (the default) rather than logging a warning
    /// and continuing. Set to <see langword="false"/> to keep the guard advisory.
    /// </summary>
    public bool FailOnViolation { get; set; } = true;

    /// <summary>
    /// Environment names the guard runs in. Defaults to Development and Staging so a
    /// misconfigured Production deployment never fails to boot over a false positive.
    /// </summary>
    public string[] Environments { get; set; } = ["Development", "Staging"];

    /// <summary>
    /// Glob-style patterns (<c>*</c> matches any run of characters, <c>:</c> is the
    /// configuration path separator, matched case-insensitively) for keys treated as
    /// sensitive. A key matching any pattern is a candidate; whether it is flagged
    /// also depends on where its value originates and, for connection strings, on
    /// whether the value actually carries a credential.
    /// </summary>
    public string[] SensitiveKeyPatterns { get; set; } =
    [
        "ConnectionStrings:*",
        "*Secret",
        "*Password",
        "*Pwd",
        "*ApiKey",
        "*AccessKey",
        "*SecretKey",
        "*PrivateKey",
        "*Token",
    ];
}
