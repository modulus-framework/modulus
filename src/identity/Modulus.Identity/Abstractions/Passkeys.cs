namespace Modulus.Identity.Abstractions;

using System.Text.Json;

/// <summary>
/// Settings of passkey sign-in (<c>Identity:Passkeys</c>, see <c>AddModulusPasskeys</c>): WebAuthn credentials that replace the
/// password and the second factor in one gesture. Off by default.
/// </summary>
public sealed class PasskeyOptions
{
    /// <summary>Turns passkeys on. When false, nothing is registered and the grant is refused.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The relying party id: the registrable domain the passkeys are bound to (for example <c>erp.example.com</c> or <c>example.com</c>).
    /// A passkey works only on this domain and its subdomains, and cannot be moved. Required when enabled.
    /// </summary>
    public string RelyingPartyId { get; set; } = "";

    /// <summary>The name the browser shows when asking to create a passkey. Defaults to the relying party id.</summary>
    public string? RelyingPartyName { get; set; }

    /// <summary>The origins allowed to run the ceremony (<c>https://erp.example.com</c>; <c>http://localhost:5173</c> in development). Required when enabled.</summary>
    public List<string> Origins { get; set; } = [];

    /// <summary>How long a ceremony may take between the options and the answer. Default 300 seconds.</summary>
    public int ChallengeLifetimeSeconds { get; set; } = 300;

    /// <summary>The most passkeys one account may have. Default 10.</summary>
    public int MaxPasskeysPerUser { get; set; } = 10;

    /// <summary>
    /// Adding or removing a passkey needs a sign-in no older than this many minutes (default 15), so a stolen token cannot plant
    /// a passkey of the attacker's. A token without <c>auth_time</c> (issued before this was added) is refused.
    /// </summary>
    public int ManageMaxAgeMinutes { get; set; } = 15;

    internal bool IsValid(out string? problem)
    {
        problem = null;
        if (!Enabled)
            return true;
        if (string.IsNullOrWhiteSpace(RelyingPartyId))
            problem = "Identity:Passkeys:RelyingPartyId is required.";
        else if (Origins.Count == 0 || Origins.Any(o => !Uri.TryCreate(o, UriKind.Absolute, out var u) || (u.Scheme != "https" && !u.IsLoopback)))
            problem = "Identity:Passkeys:Origins needs at least one https origin (http only for localhost).";
        else if (ChallengeLifetimeSeconds is < 30 or > 3600)
            problem = "Identity:Passkeys:ChallengeLifetimeSeconds must be between 30 and 3600.";
        else if (MaxPasskeysPerUser < 1 || ManageMaxAgeMinutes < 1)
            problem = "Identity:Passkeys:MaxPasskeysPerUser and ManageMaxAgeMinutes must be at least 1.";
        return problem is null;
    }
}

/// <summary>Names of the passkey grant.</summary>
public static class PasskeyGrant
{
    /// <summary>The <c>grant_type</c> of a passkey sign-in.</summary>
    public const string GrantType = "urn:modulus:params:oauth:grant-type:passkey";

    /// <summary>The token request parameter holding the challenge id returned by <c>POST /account/passkeys/login/options</c>.</summary>
    public const string ChallengeParameter = "challenge_id";

    /// <summary>The token request parameter holding the browser's assertion (the JSON of <c>PublicKeyCredential</c>).</summary>
    public const string AssertionParameter = "assertion";
}

/// <summary>A WebAuthn credential of an account. The private key never leaves the person's device.</summary>
public class ModulusPasskey
{
    /// <summary>The row id (what the API shows and removes).</summary>
    public Guid Id { get; set; }

    /// <summary>The account.</summary>
    public Guid UserId { get; set; }

    /// <summary>The credential id the authenticator presents (unique).</summary>
    public byte[] CredentialId { get; set; } = [];

    /// <summary>The public key.</summary>
    public byte[] PublicKey { get; set; } = [];

    /// <summary>The user handle stored with the credential (the account id bytes).</summary>
    public byte[] UserHandle { get; set; } = [];

    /// <summary>The signature counter; a counter that does not grow marks a cloned authenticator.</summary>
    public long SignCount { get; set; }

    /// <summary>A name the person gave it (for example "Work laptop").</summary>
    public string Name { get; set; } = "";

    /// <summary>When it was registered.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When it was last used to sign in.</summary>
    public DateTimeOffset? LastUsedAt { get; set; }

    /// <summary>The authenticator model.</summary>
    public Guid AaGuid { get; set; }

    /// <summary>Whether the credential can be synced to the person's other devices.</summary>
    public bool BackupEligible { get; set; }

    /// <summary>Whether it is currently synced (a software-held key rather than one bound to hardware).</summary>
    public bool BackedUp { get; set; }

    /// <summary>The transports the authenticator reported, comma separated.</summary>
    public string? Transports { get; set; }
}

/// <summary>A passkey ceremony in progress: the options sent to the browser, kept until the answer arrives or the time runs out.</summary>
public class ModulusPasskeyChallenge
{
    /// <summary>The id the client sends back.</summary>
    public Guid Id { get; set; }

    /// <summary><c>register</c> or <c>login</c>.</summary>
    public string Purpose { get; set; } = "";

    /// <summary>The account a registration is for; null for a sign-in.</summary>
    public Guid? UserId { get; set; }

    /// <summary>The options as sent to the browser (they hold the challenge).</summary>
    public string Options { get; set; } = "";

    /// <summary>When the ceremony is no longer accepted (UTC; a plain <see cref="DateTime"/> so every provider can compare it in a query).</summary>
    public DateTime ExpiresAt { get; set; }
}

/// <summary>Options for the browser plus the id to send back with its answer.</summary>
/// <param name="ChallengeId">The ceremony id.</param>
/// <param name="Options">The WebAuthn options object to hand to <c>navigator.credentials</c>.</param>
public sealed record PasskeyCeremony(Guid ChallengeId, JsonElement Options);

/// <summary>A passkey as listed to its owner.</summary>
/// <param name="Id">The row id.</param>
/// <param name="Name">The name.</param>
/// <param name="CreatedAt">When it was registered.</param>
/// <param name="LastUsedAt">When it last signed in.</param>
/// <param name="Synced">Whether it is a synced (software-held) credential.</param>
public sealed record PasskeyInfo(Guid Id, string Name, DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt, bool Synced);

/// <summary>Registers and verifies passkeys.</summary>
public interface IPasskeyService
{
    /// <summary>Starts registering a passkey for <paramref name="userId"/>; null when the account does not exist. Throws <see cref="InvalidOperationException"/> at the limit.</summary>
    Task<PasskeyCeremony?> BeginRegistrationAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Finishes the registration with the browser's attestation; null when it could not be verified.</summary>
    Task<PasskeyInfo?> CompleteRegistrationAsync(Guid userId, Guid challengeId, string name, string attestationJson, CancellationToken ct = default);

    /// <summary>Starts a sign-in with a discoverable credential (no user name asked).</summary>
    Task<PasskeyCeremony> BeginLoginAsync(CancellationToken ct = default);

    /// <summary>
    /// Verifies the assertion and returns the account to sign in (the same result type as the password grant, with the
    /// authentication methods <c>hwk</c>/<c>swk</c>, <c>user</c> and <c>mfa</c>). A denied result carries no reason.
    /// </summary>
    Task<PasswordGrantResult> ValidateLoginAsync(Guid challengeId, string assertionJson, CancellationToken ct = default);

    /// <summary>The account's passkeys.</summary>
    Task<IReadOnlyList<PasskeyInfo>> ListAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Removes one of the account's passkeys; false when it is not theirs.</summary>
    Task<bool> RemoveAsync(Guid userId, Guid passkeyId, CancellationToken ct = default);
}
