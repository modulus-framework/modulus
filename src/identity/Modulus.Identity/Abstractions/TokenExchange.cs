namespace Modulus.Identity.Abstractions;

/// <summary>
/// RFC 8693 token exchange (A9-1, spec 12A): an agent (an integration client) acts for a signed-in person. The person's token is
/// the subject token, the agent's own client-credentials token is the actor token, and the issued token keeps the person as its
/// subject and names the agent in its <c>act</c> claim, so the person's access is capped by the agent's.
/// </summary>
public static class TokenExchangeGrant
{
    /// <summary>The <c>grant_type</c> of a token exchange.</summary>
    public const string GrantType = "urn:ietf:params:oauth:grant-type:token-exchange";

    /// <summary>The token request parameter holding the person's access token.</summary>
    public const string SubjectTokenParameter = "subject_token";

    /// <summary>The token request parameter holding the agent's own access token.</summary>
    public const string ActorTokenParameter = "actor_token";

    /// <summary>The RFC 8693 type of an access token, used for the subject, actor and requested token types.</summary>
    public const string AccessTokenType = "urn:ietf:params:oauth:token-type:access_token";

    /// <summary>The longest an exchanged token lives, whatever the person's token has left.</summary>
    public const int MaxLifetimeMinutes = 15;
}

/// <summary>Settings for token exchange, bound from <c>Identity:TokenExchange</c>. Off unless <see cref="Enabled"/> is set.</summary>
public sealed class TokenExchangeOptions
{
    /// <summary>Whether agents may exchange a person's token. Each agent is also gated by its own act-for-people flag.</summary>
    public bool Enabled { get; set; }
}
