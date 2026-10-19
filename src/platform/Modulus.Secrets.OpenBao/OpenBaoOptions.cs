namespace Modulus.Secrets.OpenBao;

/// <summary>How the process proves its identity to OpenBao.</summary>
public enum OpenBaoAuthMethod
{
    /// <summary>A Kubernetes service-account JWT, read from <see cref="OpenBaoAuthOptions.JwtPath"/> at each login.</summary>
    Kubernetes,

    /// <summary>AppRole role id and secret id, read from files at each login.</summary>
    AppRole,

    /// <summary>A pre-issued token read from <see cref="OpenBaoAuthOptions.TokenPath"/>, re-read when it is refreshed.</summary>
    TokenFile,
}

/// <summary>One OpenBao identity: how it logs in and the paths of its credentials.</summary>
public sealed class OpenBaoAuthOptions
{
    /// <summary>The authentication method.</summary>
    public OpenBaoAuthMethod Method { get; set; } = OpenBaoAuthMethod.Kubernetes;

    /// <summary>The OpenBao role the identity logs in as (Kubernetes and AppRole).</summary>
    public string? Role { get; set; }

    /// <summary>The auth mount, for example <c>kubernetes</c> or <c>approle</c>. Unset, it follows the method.</summary>
    public string? MountPath { get; set; }

    /// <summary>The auth mount used for the login call: <see cref="MountPath"/>, else the method's default mount.</summary>
    internal string LoginMount => MountPath ?? (Method == OpenBaoAuthMethod.AppRole ? "approle" : "kubernetes");

    /// <summary>The service-account JWT file (Kubernetes).</summary>
    public string? JwtPath { get; set; } = "/var/run/secrets/kubernetes.io/serviceaccount/token";

    /// <summary>The AppRole role id file.</summary>
    public string? RoleIdPath { get; set; }

    /// <summary>The AppRole secret id file.</summary>
    public string? SecretIdPath { get; set; }

    /// <summary>The token file (TokenFile).</summary>
    public string? TokenPath { get; set; }
}

/// <summary>
/// Settings of the OpenBao provider (section <c>Secrets:OpenBao</c>). The runtime identity reads through <see cref="Auth"/>;
/// the provisioning identity (<see cref="Writer"/>) is used only by a provisioning host.
/// </summary>
public sealed class OpenBaoOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Secrets:OpenBao";

    /// <summary>The server address, for example <c>https://openbao.internal:8200</c>.</summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>The KV v2 mount, for example <c>modulus-prod</c>.</summary>
    public string Mount { get; set; } = "modulus";

    /// <summary>
    /// The namespace every secret reference must start with, for example <c>tenant-db</c>. A reference outside it is refused
    /// before any request is sent.
    /// </summary>
    public string PathPrefix { get; set; } = "tenant-db";

    /// <summary>An OpenBao namespace (sent as <c>X-Vault-Namespace</c>), when the server uses namespaces.</summary>
    public string? Namespace { get; set; }

    /// <summary>A PEM file with the CA that signed the OpenBao listener, when it is not in the system store.</summary>
    public string? CaCertificatePath { get; set; }

    /// <summary>Allows an <c>http://</c> address. Development only; any other environment refuses it.</summary>
    public bool AllowInsecureHttp { get; set; }

    /// <summary>The timeout of one OpenBao request.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>The runtime identity (read-only policy).</summary>
    public OpenBaoAuthOptions Auth { get; set; } = new();

    /// <summary>The provisioning identity (create and update policy). Set only on a provisioning host.</summary>
    public OpenBaoAuthOptions? Writer { get; set; }

    /// <summary>Checks the settings; returns the first problem, or <c>null</c>.</summary>
    public string? Validate()
    {
        if (!Uri.TryCreate(Address, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            return "Secrets:OpenBao:Address must be an absolute http or https URL.";
        if (uri.Scheme == Uri.UriSchemeHttp && !AllowInsecureHttp)
            return "Secrets:OpenBao:Address must use https. Set AllowInsecureHttp only in Development.";
        if (!IsSafeSegment(Mount))
            return "Secrets:OpenBao:Mount must contain only lower-case letters, digits and hyphens.";
        if (!IsSafeSegment(PathPrefix))
            return "Secrets:OpenBao:PathPrefix must contain only lower-case letters, digits and hyphens.";
        if (RequestTimeout <= TimeSpan.Zero)
            return "Secrets:OpenBao:RequestTimeout must be greater than zero.";
        return Auth.Validate("Secrets:OpenBao:Auth") ?? Writer?.Validate("Secrets:OpenBao:Writer:Auth");
    }

    private static bool IsSafeSegment(string value)
        => value.Length is > 0 and <= 64 && value.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-');
}

/// <summary>Validation of one <see cref="OpenBaoAuthOptions"/>.</summary>
internal static class OpenBaoAuthValidation
{
    /// <summary>Returns the first problem with <paramref name="auth"/>, or <c>null</c>.</summary>
    public static string? Validate(this OpenBaoAuthOptions auth, string section)
    {
        ArgumentNullException.ThrowIfNull(auth);
        return auth.Method switch
        {
            OpenBaoAuthMethod.Kubernetes when string.IsNullOrWhiteSpace(auth.Role) || string.IsNullOrWhiteSpace(auth.JwtPath)
                => $"{section}: Kubernetes login needs Role and JwtPath.",
            OpenBaoAuthMethod.AppRole when string.IsNullOrWhiteSpace(auth.Role) || string.IsNullOrWhiteSpace(auth.RoleIdPath) || string.IsNullOrWhiteSpace(auth.SecretIdPath)
                => $"{section}: AppRole login needs Role, RoleIdPath and SecretIdPath.",
            OpenBaoAuthMethod.TokenFile when string.IsNullOrWhiteSpace(auth.TokenPath)
                => $"{section}: TokenFile needs TokenPath.",
            _ => null,
        };
    }
}
