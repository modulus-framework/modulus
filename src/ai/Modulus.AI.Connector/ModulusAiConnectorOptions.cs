namespace Modulus.AI.Connector;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// Settings of the AI connector (section <c>Ai:Connector</c>): how the AI platform authenticates to this app, which
/// platform app instances map to which company, and where to send revocation signals.
/// </summary>
public sealed class ModulusAiConnectorOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Ai:Connector";

    /// <summary>Whether the endpoints answer at all. Off: every connector call gets <c>404</c>.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Where the wire contract is mapped. Framework endpoints live under <c>/_ai</c>.</summary>
    public string PathPrefix { get; set; } = "/_ai/connector";

    /// <summary>The wire contract major version this app implements (AD-10: versioned by major).</summary>
    public string ContractVersion { get; set; } = "1";

    /// <summary>The app type the manifest declares (<c>erp</c>, <c>crm</c>, ...).</summary>
    [Required]
    public string AppType { get; set; } = "erp";

    /// <summary>The app's display name in the manifest.</summary>
    public string? AppName { get; set; }

    /// <summary>
    /// SHA-256 hashes (hex) of the API keys the platform presents as <c>Authorization: ApiKey &lt;key&gt;</c>; at most
    /// two, so a key can be rotated without downtime. The keys themselves are never stored (compute a hash with
    /// <see cref="AiApiKeys.Hash"/>).
    /// </summary>
    public List<string> ApiKeyHashes { get; set; } = [];

    /// <summary>The platform side: envelope signing keys, issuer and the revocation endpoint.</summary>
    public AiPlatformOptions Platform { get; set; } = new();

    /// <summary>The platform app instances this app serves, each mapped to one company.</summary>
    public List<AiAppInstanceOptions> Instances { get; set; } = [];

    /// <summary>How the envelope's user is matched to a Modulus account.</summary>
    public AiUserMatchOptions Users { get; set; } = new();

    /// <summary>
    /// Whether a mapped company also requires the user's active membership in it (the default). Turn off only for an
    /// app whose companies have no membership store.
    /// </summary>
    public bool RequireMembership { get; set; } = true;

    /// <summary>
    /// The platform classification of a field with no classification attribute. Defaults to
    /// <see cref="AiDataClass.Confidential"/>, so an unreviewed field is never treated as public.
    /// </summary>
    public AiDataClass DefaultClassification { get; set; } = AiDataClass.Confidential;

    /// <summary>The longest capability or resource description the manifest accepts (FR-18a).</summary>
    [Range(16, 4000)]
    public int MaxDescriptionLength { get; set; } = 500;

    /// <summary>The most records one capability call returns; the rest are cut and the result is marked truncated.</summary>
    [Range(1, 10_000)]
    public int MaxResults { get; set; } = 200;

    /// <summary>The most records one <c>/authz/resources:check</c> call may name.</summary>
    [Range(1, 1000)]
    public int MaxBatchSize { get; set; } = 100;

    /// <summary>How long one call may run before it answers <c>UNAVAILABLE</c> (the platform treats that as a deny).</summary>
    public TimeSpan CallTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>The scope lifetime the authorization adapter reports. The platform caps it at 5 minutes (FR-17).</summary>
    public TimeSpan ScopeTtl { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Prefix for relative deep links (<c>https://erp.example.com</c>). Without it, deep links stay relative.
    /// </summary>
    public string? PublicBaseUrl { get; set; }

    /// <summary>The most filters one generated <c>Search</c> or <c>Calculate</c> call may carry.</summary>
    [Range(1, 50)]
    public int MaxFilters { get; set; } = 10;

    /// <summary>The most groups one generated <c>Calculate</c> call returns (largest values first).</summary>
    [Range(1, 1000)]
    public int MaxGroups { get; set; } = 50;

    /// <summary>Extraction, the change feed and change hints (<c>/extract</c>, <c>/changes</c>).</summary>
    public AiIndexingOptions Indexing { get; set; } = new();

    /// <summary>
    /// When true, a field marked <c>[PersonalInformation]</c> or <c>[ProtectedPersonalData]</c> is treated like a Restricted field for
    /// whoever the platform asks as: only a user holding the clearance the entity's field-security profile requires for
    /// <c>Restricted</c> (or for that field) receives it, and without a profile it is withheld from everyone (fail closed).
    /// Filtering or sorting on a withheld field is refused. Default false: such fields are only declared <c>Restricted</c> in the manifest.
    /// </summary>
    public bool MaskPersonalInformation { get; set; }
}

/// <summary>
/// How the platform's ingestion reads this app. Ingestion runs as a <b>service identity</b> per app instance, not as any
/// user (Architecture §8): a principal holding <see cref="Roles"/> in the instance's company, so the app's own grant
/// store decides what it may read (grant the role the read permissions of the indexed resource types). Every record
/// is re-authorized per user when it is used in an answer.
/// </summary>
public sealed class AiIndexingOptions
{
    /// <summary>The roles of the indexing identity. Grant them exactly what the index may hold.</summary>
    public List<string> Roles { get; set; } = ["AiIndexer"];

    /// <summary>The account id the indexing identity carries, when the grant store keys grants by user too.</summary>
    public Guid? ServiceUserId { get; set; }

    /// <summary>The default and largest page of <c>/extract</c> and <c>/changes</c>.</summary>
    [Range(1, 1000)]
    public int PageSize { get; set; } = 100;

    /// <summary>
    /// How old a journaled change must be before <c>/changes</c> returns it, so a transaction that wrote an earlier
    /// sequence number and is still committing is not skipped. A transaction longer than this can still be missed
    /// until the next full extraction.
    /// </summary>
    public TimeSpan ChangesSettleDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Change hints: a signed "this instance changed" call to the platform (never data, AD-27), so it calls
    /// <c>/changes</c> sooner than its schedule.
    /// </summary>
    public AiChangeHintOptions ChangeHints { get; set; } = new();
}

/// <summary>Signed change hints to the platform (Standard Webhooks signature, HMAC-SHA256).</summary>
public sealed class AiChangeHintOptions
{
    /// <summary>Whether hints are sent. Needs <see cref="AiPlatformOptions.WebhookSecret"/>.</summary>
    public bool Enabled { get; set; }

    /// <summary>How often the journal head of every instance is checked.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>The platform path hints are posted to, relative to <see cref="AiPlatformOptions.BaseUrl"/>.</summary>
    public string Path { get; set; } = "/webhooks/app-changes";
}

/// <summary>The AI platform side of the connection.</summary>
public sealed class AiPlatformOptions
{
    /// <summary>The <c>iss</c> of the platform's envelopes.</summary>
    public string? Issuer { get; set; }

    /// <summary>Where the platform publishes its envelope signing keys (a JWKS document).</summary>
    public string? JwksUrl { get; set; }

    /// <summary>The signing keys as an inline JWKS document (offline setups and tests); used with or instead of <see cref="JwksUrl"/>.</summary>
    public string? SigningKeys { get; set; }

    /// <summary>How long fetched keys are kept before they are fetched again.</summary>
    public TimeSpan KeyRefreshInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>The request header that carries the envelope.</summary>
    public string EnvelopeHeader { get; set; } = "AiPlatform-Envelope";

    /// <summary>Clock skew tolerated on the envelope's lifetime.</summary>
    public TimeSpan ClockSkew { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>The longest envelope lifetime accepted (<c>exp - iat</c>); the platform issues about 60 seconds.</summary>
    public TimeSpan MaxEnvelopeLifetime { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>The platform's base URL; revocation signals go to <c>{BaseUrl}/revocations/scope</c>.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>The API key this app presents to the platform (a secret: user secrets, environment or a vault).</summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// The secret that signs change hints (<c>whsec_</c> + base64, or base64), the one the platform's Settings show for
    /// the app's webhooks. A secret: user secrets, environment or a vault.
    /// </summary>
    public string? WebhookSecret { get; set; }

    /// <summary>The longest wait between two attempts to deliver one revocation signal.</summary>
    public TimeSpan RevocationMaxBackoff { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// A file that journals the revocation signals not yet acknowledged, so a restart or crash does not lose them (they are
    /// sent again on the next start). One file per process; null keeps the signals in memory only, where the platform's
    /// five-minute scope expiry bounds the effect of a lost one.
    /// </summary>
    public string? RevocationSpoolFile { get; set; }
}

/// <summary>One platform app instance served by this app.</summary>
public sealed class AiAppInstanceOptions
{
    /// <summary>The platform's app instance id (the envelope's audience and <c>app_instance_id</c>).</summary>
    public string AppInstanceId { get; set; } = string.Empty;

    /// <summary>The platform tenant that owns the instance (the envelope's <c>tenant_id</c>).</summary>
    public string PlatformTenantId { get; set; } = string.Empty;

    /// <summary>
    /// The Modulus company the instance reads. Null for an app without companies; with multi-tenancy on, a null company
    /// sees nothing (the connector never enters the host context).
    /// </summary>
    public Guid? TenantId { get; set; }
}

/// <summary>How the envelope's user is found among the app's accounts.</summary>
public sealed class AiUserMatchOptions
{
    /// <summary>The envelope claim that identifies the user.</summary>
    public string Claim { get; set; } = AiEnvelopeClaims.Subject;

    /// <summary>What the claim's value is matched against.</summary>
    public AiUserMatch MatchBy { get; set; } = AiUserMatch.Id;
}

/// <summary>What the envelope's user claim is matched against.</summary>
public enum AiUserMatch
{
    /// <summary>The account id (the platform federates with this app's identity provider).</summary>
    Id,

    /// <summary>The account's e-mail address.</summary>
    Email,

    /// <summary>The account's user name.</summary>
    UserName,
}

/// <summary>The platform's data classes (BRS §6.2). <c>Secret</c> has no class: it is never sent.</summary>
public enum AiDataClass
{
    /// <summary>Anyone may see it.</summary>
    Public,

    /// <summary>Any user of the company may see it.</summary>
    Internal,

    /// <summary>Only users the permission model allows.</summary>
    Confidential,

    /// <summary>The most sensitive data; never embedded in the platform's index (AD-06).</summary>
    Restricted,
}
