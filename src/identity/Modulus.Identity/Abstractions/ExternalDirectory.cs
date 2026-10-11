namespace Modulus.Identity.Abstractions;

/// <summary>
/// Settings of directory sign-in (<c>Identity:Ldap</c>, see <c>AddModulusLdap</c>): the password grant checks the password by
/// binding to an LDAP / Active Directory server instead of a local hash. Off by default.
/// </summary>
public sealed class LdapOptions
{
    /// <summary>Turns directory sign-in on.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// A user name that is in none of the directories signs in with a local password (a break-glass administrator, a
    /// shop-floor operator). A user name found in a directory never falls back: a wrong directory password is refused even when
    /// an old local one would match. Default true.
    /// </summary>
    public bool AllowLocalFallback { get; set; } = true;

    /// <summary>The directories, asked in order; the first that has the user decides.</summary>
    public List<LdapDirectoryOptions> Directories { get; set; } = [];

    internal bool IsValid(out string? problem)
    {
        problem = null;
        if (!Enabled)
            return true;
        if (Directories.Count == 0)
            problem = "Identity:Ldap:Directories needs at least one directory.";
        else if (Directories.Select(d => d.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Directories.Count)
            problem = "Identity:Ldap:Directories names must be unique.";
        else
            problem = Directories.Select(d => d.Validate()).FirstOrDefault(p => p is not null);
        return problem is null;
    }
}

/// <summary>One LDAP / Active Directory server and how its people map to accounts.</summary>
public sealed class LdapDirectoryOptions
{
    /// <summary>A short name, unique among the directories. It is part of the account link, so renaming it unlinks every account.</summary>
    public string Name { get; set; } = "";

    /// <summary>The server's host name.</summary>
    public string Host { get; set; } = "";

    /// <summary>The port; 0 picks 636 with <see cref="UseSsl"/> and 389 otherwise.</summary>
    public int Port { get; set; }

    /// <summary>LDAPS: TLS from the first byte.</summary>
    public bool UseSsl { get; set; }

    /// <summary>StartTLS on a plain connection, before any password is sent.</summary>
    public bool UseStartTls { get; set; }

    /// <summary>
    /// Allows an unencrypted connection to a host other than localhost. Passwords then cross the network in clear text, so
    /// leave it off; startup refuses a directory with neither TLS option unless this is set.
    /// </summary>
    public bool AllowInsecure { get; set; }

    /// <summary>The distinguished name the user search starts under, for example <c>OU=People,DC=example,DC=com</c>.</summary>
    public string BaseDn { get; set; } = "";

    /// <summary>The account that searches for the user (read-only; it never signs anyone in). Empty = anonymous search.</summary>
    public string BindDn { get; set; } = "";

    /// <summary>The search account's password. A secret: user secrets or the environment, never a committed file.</summary>
    public string BindPassword { get; set; } = "";

    /// <summary>
    /// The filter that finds one person; <c>{0}</c> is the typed user name, escaped (RFC 4515). The default suits Active
    /// Directory; OpenLDAP usually uses <c>(&amp;(objectClass=inetOrgPerson)(uid={0}))</c>.
    /// </summary>
    public string UserFilter { get; set; } = "(&(objectClass=user)(sAMAccountName={0}))";

    /// <summary>The attribute that never changes for a person (it links the account). Default <c>objectGUID</c> (<c>entryUUID</c> on OpenLDAP).</summary>
    public string SubjectAttribute { get; set; } = "objectGUID";

    /// <summary>The attribute used as the account's user name. Default <c>sAMAccountName</c>.</summary>
    public string UserNameAttribute { get; set; } = "sAMAccountName";

    /// <summary>The mail attribute. Default <c>mail</c>.</summary>
    public string EmailAttribute { get; set; } = "mail";

    /// <summary>The given-name attribute. Default <c>givenName</c>.</summary>
    public string FirstNameAttribute { get; set; } = "givenName";

    /// <summary>The surname attribute. Default <c>sn</c>.</summary>
    public string LastNameAttribute { get; set; } = "sn";

    /// <summary>The attribute listing the person's groups. Default <c>memberOf</c>.</summary>
    public string GroupAttribute { get; set; } = "memberOf";

    /// <summary>The company the directory's people belong to; null = host-level accounts.</summary>
    public Guid? TenantId { get; set; }

    /// <summary>Creates the account on the first successful sign-in. When false only people who already have an account (linked) get in. Default true.</summary>
    public bool AutoProvision { get; set; } = true;

    /// <summary>
    /// Links a first sign-in to an existing local account with the same e-mail address. Only turn it on when the directory
    /// is the authority for the address: anyone who can set <c>mail</c> in the directory could take the account over. Default false.
    /// </summary>
    public bool LinkExistingByEmail { get; set; }

    /// <summary>
    /// Directory group → role. The key is the group's full distinguished name or just its common name (case-insensitive).
    /// At every sign-in the person gets the roles of their groups and loses the mapped roles of groups they left; roles that
    /// are not a value here are never touched. Empty = roles are not managed by the directory.
    /// </summary>
    public Dictionary<string, string> GroupRoles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>How long a connection or search may take. Default 10 seconds.</summary>
    public int TimeoutSeconds { get; set; } = 10;

    internal string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
            return "Identity:Ldap:Directories:Name is required.";
        if (string.IsNullOrWhiteSpace(Host) || string.IsNullOrWhiteSpace(BaseDn))
            return $"Identity:Ldap directory '{Name}' needs Host and BaseDn.";
        if (!UserFilter.Contains("{0}", StringComparison.Ordinal))
            return $"Identity:Ldap directory '{Name}': UserFilter must contain {{0}}, the user name.";
        if (!UseSsl && !UseStartTls && !AllowInsecure && !IsLocal(Host))
            return $"Identity:Ldap directory '{Name}' would send passwords unencrypted: set UseSsl or UseStartTls.";
        if (TimeoutSeconds is < 1 or > 120)
            return $"Identity:Ldap directory '{Name}': TimeoutSeconds must be between 1 and 120.";
        return null;
    }

    internal int EffectivePort => Port > 0 ? Port : UseSsl ? 636 : 389;

    private static bool IsLocal(string host)
        => host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host is "127.0.0.1" or "::1";
}

/// <summary>A person found in a directory.</summary>
/// <param name="DistinguishedName">The entry's DN, used to bind as the person.</param>
/// <param name="Attributes">The requested attributes (names are case-insensitive); a binary GUID is returned as text.</param>
public sealed record LdapUserEntry(string DistinguishedName, IReadOnlyDictionary<string, IReadOnlyList<string>> Attributes)
{
    /// <summary>The first value of an attribute, or null.</summary>
    public string? First(string attribute)
        => Attributes.TryGetValue(attribute, out var values) && values.Count > 0 && !string.IsNullOrWhiteSpace(values[0]) ? values[0] : null;

    /// <summary>All values of an attribute.</summary>
    public IReadOnlyList<string> All(string attribute) => Attributes.TryGetValue(attribute, out var values) ? values : [];
}

/// <summary>
/// The two directory operations sign-in needs. The default talks LDAP with <c>System.DirectoryServices.Protocols</c>; an app can
/// replace it (another client library, a directory gateway) and tests use a fake.
/// </summary>
public interface ILdapDirectoryClient
{
    /// <summary>Finds the person with the search account; null when there is no such person or more than one.</summary>
    Task<LdapUserEntry?> FindUserAsync(LdapDirectoryOptions directory, string userName, CancellationToken ct = default);

    /// <summary>True when the directory accepts the password for that entry; false for a wrong password. Throws when the server cannot be reached.</summary>
    Task<bool> BindAsync(LdapDirectoryOptions directory, string distinguishedName, string password, CancellationToken ct = default);
}

/// <summary>LDAP filter helpers.</summary>
public static class LdapFilter
{
    /// <summary>
    /// Escapes a value for use inside a filter (RFC 4515): <c>\ * ( )</c> and NUL become <c>\xx</c>, so a typed user name cannot
    /// change the meaning of the search (<c>*)(objectClass=*</c> is just a name that does not exist).
    /// </summary>
    public static string Escape(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var sb = new System.Text.StringBuilder(value.Length + 8);
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\': sb.Append("\\5c"); break;
                case '*': sb.Append("\\2a"); break;
                case '(': sb.Append("\\28"); break;
                case ')': sb.Append("\\29"); break;
                case '\0': sb.Append("\\00"); break;
                default: sb.Append(c); break;
            }
        }

        return sb.ToString();
    }
}
