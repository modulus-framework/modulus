namespace ModulusSample.Shared.Application.Abstractions.Oidc;

/// <summary>
/// Represents an OIDC token response
/// </summary>
public sealed class OidcTokenResponse
{
    /// <summary>
    /// Gets the access token
    /// </summary>
    public string AccessToken { get; }

    /// <summary>
    /// Gets the refresh token
    /// </summary>
    public string RefreshToken { get; }

    /// <summary>
    /// Gets the ID token
    /// </summary>
    public string IdToken { get; }

    /// <summary>
    /// Gets the access token expiration time in seconds
    /// </summary>
    public long ExpiresIn { get; }

    /// <summary>
    /// Gets the refresh token expiration time in seconds
    /// </summary>
    public long RefreshExpiresIn { get; }

    /// <summary>
    /// Gets the token type
    /// </summary>
    public string TokenType { get; }

    /// <summary>
    /// Initializes a new instance of the OidcTokenResponse class
    /// </summary>
    /// <param name="accessToken">The access token</param>
    /// <param name="refreshToken">The refresh token</param>
    /// <param name="idToken">The ID token</param>
    /// <param name="expiresIn">The access token expiration time in seconds</param>
    /// <param name="refreshExpiresIn">The refresh token expiration time in seconds</param>
    /// <param name="tokenType">The token type</param>
    public OidcTokenResponse(
        string accessToken,
        string refreshToken,
        string idToken,
        long expiresIn,
        long refreshExpiresIn,
        string tokenType)
    {
        AccessToken = accessToken ?? throw new ArgumentNullException(nameof(accessToken));
        RefreshToken = refreshToken ?? throw new ArgumentNullException(nameof(refreshToken));
        IdToken = idToken ?? throw new ArgumentNullException(nameof(idToken));
        ExpiresIn = expiresIn;
        RefreshExpiresIn = refreshExpiresIn;
        TokenType = tokenType ?? throw new ArgumentNullException(nameof(tokenType));
    }
}
