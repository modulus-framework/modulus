using ModulusSample.Shared.Application.Abstractions;
using ModulusSample.Shared.Application.Abstractions.Oidc;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Reflection;

namespace ModulusSample.Shared.Infrastructure.Authentication;

public static class AuthenticationExtensions
{
    public static AuthenticationBuilder AddDualAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<OidcOptions>()
            .Bind(configuration.GetSection(OidcOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpContextAccessor();
        services.AddScoped<ITokenBlacklistService, TokenBlacklistService>();

        AuthenticationBuilder builder = services.AddAuthentication("DualBearer")
            .AddScheme<DualBearerOptions, DualBearerAuthenticationHandler>("DualBearer", null);

        IConfigurationSection oidcSection = configuration.GetSection(OidcOptions.SectionName);
        string? issuerUrl = oidcSection["IssuerUrl"];

        if (!string.IsNullOrWhiteSpace(issuerUrl))
        {
            builder.AddExternalOidc(oidcSection);
        }

        return builder;
    }

    private static AuthenticationBuilder AddExternalOidc(
        this AuthenticationBuilder builder,
        IConfigurationSection oidcSection)
    {
        string issuerUrl = oidcSection["IssuerUrl"] ?? throw new InvalidOperationException("OIDC IssuerUrl is not configured");
        string clientId = oidcSection["ClientId"]
            ?? throw new InvalidOperationException("OIDC ClientId is not configured");

        string? audience = oidcSection["Audience"];
        string? appSlug = oidcSection["AppSlug"];
        bool requireHttpsMetadata = oidcSection.GetValue("RequireHttpsMetadata", true);

        string publicAuthority = $"{issuerUrl.TrimEnd('/')}/";

        string? internalMetadataAddress = oidcSection["MetadataAddress"];
        if (string.IsNullOrEmpty(internalMetadataAddress))
        {
            internalMetadataAddress = !string.IsNullOrEmpty(appSlug)
                ? $"{issuerUrl.TrimEnd('/')}/{appSlug.Trim('/')}/.well-known/openid-configuration"
                : $"{issuerUrl.TrimEnd('/')}/.well-known/openid-configuration";
        }

        return builder.AddJwtBearer("ExternalOidc", options =>
        {
            options.Authority = publicAuthority;
            options.Audience = !string.IsNullOrEmpty(audience) ? audience : clientId;
            options.RequireHttpsMetadata = requireHttpsMetadata;
            options.SaveToken = true;

            options.MetadataAddress = internalMetadataAddress;
            options.Configuration = null;

            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = publicAuthority,
                ValidAudiences = new[]
                {
                    !string.IsNullOrEmpty(audience) ? audience : clientId,
                    "account"
                },
                ClockSkew = TimeSpan.FromSeconds(oidcSection.GetValue("ClockSkewSeconds", 60)),
                NameClaimType = "name",
                RoleClaimType = "roles"
            };

            options.Events = new JwtBearerEvents
            {
                OnAuthenticationFailed = context =>
                {
                    if (context.Exception is SecurityTokenExpiredException)
                    {
                        context.Response.Headers["Token-Expired"] = "true";
                        context.Response.Headers["Access-Control-Expose-Headers"] = "Token-Expired";
                    }
                    return Task.CompletedTask;
                },

                OnTokenValidated = async context =>
                {
                    ILogger logger = context.HttpContext.RequestServices
                        .GetRequiredService<ILoggerFactory>()
                        .CreateLogger("OidcAuthentication");

                    CancellationToken ct = context.HttpContext.RequestAborted;

                    if (context.Principal != null)
                    {
                        IClaimsTransformation claimsTransformation = context.HttpContext.RequestServices
                            .GetRequiredService<IClaimsTransformation>();
                        context.Principal = await claimsTransformation.TransformAsync(context.Principal);
                    }

                    ITokenBlacklistService? blacklistService = context.HttpContext.RequestServices
                        .GetService<ITokenBlacklistService>();

                    if (blacklistService is null)
                    {
                        return;
                    }

                    string? rawToken = context.HttpContext.Request.Headers["Authorization"]
                        .FirstOrDefault()?
                        .Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase);

                    if (string.IsNullOrEmpty(rawToken))
                    {
                        return;
                    }

                    JwtSecurityToken? parsedJwt = null;
                    string? tokenId = null;

                    try
                    {
                        parsedJwt = new JwtSecurityToken(rawToken);
                        tokenId = parsedJwt.Id ?? rawToken;

                        if (await blacklistService.IsTokenBlacklistedAsync(tokenId, CancellationToken.None))
                        {
                            context.Fail("Token is blacklisted");
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to parse or check blacklist for token");
                    }

                    try
                    {
                        var userContextCache = context.HttpContext.RequestServices
                            .GetService(typeof(IUserContextCacheService)) as IUserContextCacheService;

                        if (userContextCache != null)
                        {
                            string? externalId = context.Principal?.FindFirst("sub")?.Value;
                            if (!string.IsNullOrEmpty(externalId))
                            {
                                logger.LogDebug("Looking up user by external ID: {ExternalId} for path: {Path}",
                                    externalId, context.HttpContext.Request.Path);

                                object? user = await userContextCache.GetUserByExternalIdAsync(externalId, ct);

                                if (user is null)
                                {
                                    bool isProvisionEndpoint = context.HttpContext.Request.Path
                                        .StartsWithSegments("/api/v1/auth/provision");

                                    if (isProvisionEndpoint)
                                    {
                                        logger.LogDebug(
                                            "User not found in database for external ID: {ExternalId} — allowing through for provisioning",
                                            externalId);
                                    }
                                    else
                                    {
                                        logger.LogWarning(
                                            "User not found in application database for external ID: {ExternalId}, Path: {Path} — call /api/v1/auth/provision first",
                                            externalId, context.HttpContext.Request.Path);
                                        context.Fail("User not found in application database");
                                        return;
                                    }
                                }

                                PropertyInfo? userIdProperty = user?.GetType().GetProperty("Id");
                                if (userIdProperty != null)
                                {
                                    object? userId = userIdProperty.GetValue(user);
                                    context.HttpContext.Items["User"] = user;
                                    context.HttpContext.Items["UserId"] = userId;

                                    logger.LogDebug("Resolved user context for external ID: {ExternalId}, User ID: {UserId}",
                                        externalId, userId);

                                    // Check if all user tokens are blacklisted
                                    if (userId is Guid resolvedUserId)
                                    {
                                        if (await blacklistService.AreAllUserTokensBlacklistedAsync(resolvedUserId.ToString(), ct))
                                        {
                                            logger.LogWarning("All tokens for user {UserId} are blacklisted", resolvedUserId);
                                            context.Fail("All tokens are blacklisted");
                                            return;
                                        }
                                    }
                                }
                            }
                        }
                        else
                        {
                            logger.LogDebug("IUserContextCacheService not registered, skipping user lookup");
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Error resolving user context during authentication for path: {Path}",
                            context.HttpContext.Request.Path);
                    }

                    bool isProvisionRequest = context.HttpContext.Request.Path
                        .StartsWithSegments("/api/v1/auth/provision");

                    if (!isProvisionRequest)
                    {
                        try
                        {
                            ISessionService? sessionService = context.HttpContext.RequestServices
                                .GetService<ISessionService>();

                            if (sessionService != null)
                            {
                                string? sid = context.Principal?.FindFirst("sid")?.Value;
                                if (!string.IsNullOrEmpty(sid) && context.HttpContext.Items["UserId"] is Guid sessionUserId)
                                {
                                    string? accessTokenJti = parsedJwt?.Id;
                                    DateTime expiresAt = DateTime.UtcNow.AddHours(1);

                                    if (parsedJwt != null)
                                    {
                                        string? expClaim = parsedJwt.Claims.FirstOrDefault(c => c.Type == "exp")?.Value;
                                        if (long.TryParse(expClaim, out long expSeconds))
                                        {
                                            expiresAt = DateTimeOffset.FromUnixTimeSeconds(expSeconds).UtcDateTime;
                                        }
                                    }

                                    string? userAgent = ClientInfoExtractor.GetUserAgent(context.HttpContext);
                                    string? ipAddress = ClientInfoExtractor.GetClientIpAddress(context.HttpContext);

                                    bool isSessionValid = await sessionService.EnsureSessionAsync(
                                        sessionUserId,
                                        sid,
                                        accessTokenJti,
                                        userAgent,
                                        ipAddress,
                                        expiresAt,
                                        ct);

                                    if (!isSessionValid)
                                    {
                                        logger.LogWarning(
                                            "Session {Sid} for user {UserId} has been revoked",
                                            sid, sessionUserId);
                                        context.Fail("Session has been revoked");
                                        return;
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, "Error checking session validity");
                        }
                    }
                },

                OnMessageReceived = context =>
                {
                    string? accessToken = context.Request.Query["access_token"];

                    if (!string.IsNullOrEmpty(accessToken) &&
                        context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                    {
                        context.Token = accessToken;
                    }

                    return Task.CompletedTask;
                }
            };
        });
    }
}

/// <summary>
/// Options for configuring OIDC authentication
/// </summary>
public class OidcOptions
{
    public const string SectionName = "Users:Oidc";

    /// <summary>
    /// The OIDC provider's issuer URL
    /// </summary>
    public string IssuerUrl { get; set; } = string.Empty;

    /// <summary>
    /// The OIDC client identifier
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// The expected audience in the token. Defaults to ClientId if not set.
    /// </summary>
    public string? Audience { get; set; }

    /// <summary>
    /// The application slug used to construct the default OIDC discovery URL.
    /// Only required when the provider uses a non-standard discovery URL pattern.
    /// </summary>
    public string? AppSlug { get; set; }

    /// <summary>
    /// Explicit OIDC MetadataAddress (discovery URL). If not set, the default discovery URL
    /// is constructed from IssuerUrl (and AppSlug if provided).
    /// </summary>
    public string? MetadataAddress { get; set; }

    /// <summary>
    /// Whether HTTPS is required for the metadata address. Defaults to true.
    /// </summary>
    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>
    /// Clock skew in seconds allowed for token validation. Defaults to 60.
    /// </summary>
    public int ClockSkewSeconds { get; set; } = 60;
}
