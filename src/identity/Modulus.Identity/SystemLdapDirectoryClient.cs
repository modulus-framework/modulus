namespace Modulus.Identity;

using System.DirectoryServices.Protocols;
using System.Net;
using System.Text;
using Modulus.Identity.Abstractions;

/// <summary>
/// <see cref="ILdapDirectoryClient"/> on <c>System.DirectoryServices.Protocols</c> (Windows: wldap32; Linux and macOS: libldap
/// must be installed). The library is synchronous, so each call runs on the thread pool.
/// </summary>
internal sealed class SystemLdapDirectoryClient : ILdapDirectoryClient
{
    // LDAP result code 49: invalid credentials (also what Active Directory answers for a locked or disabled account).
    private const int InvalidCredentials = 49;

    public Task<LdapUserEntry?> FindUserAsync(LdapDirectoryOptions directory, string userName, CancellationToken ct = default)
        => Task.Run(() => Find(directory, userName), ct);

    public Task<bool> BindAsync(LdapDirectoryOptions directory, string distinguishedName, string password, CancellationToken ct = default)
        => Task.Run(() => Bind(directory, distinguishedName, password), ct);

    private static LdapUserEntry? Find(LdapDirectoryOptions d, string userName)
    {
        using var connection = Connect(d);
        if (string.IsNullOrEmpty(d.BindDn))
            connection.Bind();
        else
            connection.Bind(new NetworkCredential(d.BindDn, d.BindPassword));

        var filter = d.UserFilter.Replace("{0}", LdapFilter.Escape(userName), StringComparison.Ordinal);
        string[] attributes =
        [
            d.SubjectAttribute, d.UserNameAttribute, d.EmailAttribute, d.FirstNameAttribute, d.LastNameAttribute, d.GroupAttribute,
        ];
        var request = new SearchRequest(d.BaseDn, filter, SearchScope.Subtree, attributes) { SizeLimit = 2, TimeLimit = TimeSpan.FromSeconds(d.TimeoutSeconds) };
        var response = (SearchResponse)connection.SendRequest(request);

        // Zero people: unknown. More than one: ambiguous, so nobody (guessing which would sign the wrong person in).
        if (response.Entries.Count != 1)
            return null;

        var entry = response.Entries[0];
        var values = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in entry.Attributes.AttributeNames)
            values[name] = [.. entry.Attributes[name].GetValues(typeof(byte[])).Cast<byte[]>().Select(AsText)];
        return new LdapUserEntry(entry.DistinguishedName, values);
    }

    private static bool Bind(LdapDirectoryOptions d, string distinguishedName, string password)
    {
        using var connection = Connect(d);
        try
        {
            connection.Bind(new NetworkCredential(distinguishedName, password));
            return true;
        }
        catch (LdapException ex) when (ex.ErrorCode == InvalidCredentials)
        {
            return false;
        }
    }

    private static LdapConnection Connect(LdapDirectoryOptions d)
    {
        var connection = new LdapConnection(new LdapDirectoryIdentifier(d.Host, d.EffectivePort))
        {
            AuthType = AuthType.Basic,
            Timeout = TimeSpan.FromSeconds(d.TimeoutSeconds),
        };
        connection.SessionOptions.ProtocolVersion = 3;
        // Never chase referrals: a rogue referral would receive the bind password.
        connection.SessionOptions.ReferralChasing = ReferralChasingOptions.None;
        if (d.UseSsl)
            connection.SessionOptions.SecureSocketLayer = true;
        else if (d.UseStartTls)
            connection.SessionOptions.StartTransportLayerSecurity(null);
        return connection;
    }

    // objectGUID and entryUUID come back as 16 raw bytes; everything else is UTF-8 text.
    private static string AsText(byte[] bytes)
        => bytes.Length == 16 && bytes.Any(b => b < 0x20 && b != 0x09)
            ? new Guid(bytes).ToString()
            : Encoding.UTF8.GetString(bytes);
}
