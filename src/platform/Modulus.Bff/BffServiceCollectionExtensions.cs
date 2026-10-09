namespace Modulus.Bff;

using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Modulus.Bff.Authentication;
using Modulus.Bff.Http;
using Modulus.Bff.Proxy;
using Modulus.Bff.Tokens;
using Modulus.Caching;
using Yarp.ReverseProxy.Configuration;

/// <summary>
/// Registers the Modulus BFF: one backend per client type (web SPA, mobile app, partner), each
/// with its own authentication scheme, policy, token handling, rate limit and passthrough routes,
/// against any OIDC auth server Modulus supports (OpenIddict, Keycloak, Auth0, Okta, Entra ID,
/// Duende, Authentik). Works for a modular monolith (one <c>api</c> upstream) and for
/// microservices (one upstream per service, optionally through service discovery).
/// </summary>
public static class BffServiceCollectionExtensions
{
    /// <summary>
    /// Adds the BFF services and the clients registered in <paramref name="configure"/>, e.g.
    /// <c>bff =&gt; bff.AddWebClient().AddMobileClient()</c>. Settings bind from <c>Bff</c> and
    /// <c>Bff:Clients:{name}</c>; <c>Api:BaseUrl</c> is the fallback address of the <c>api</c> service.
    /// Without a registered <see cref="ICacheService"/>, FusionCache (L1 only) is added; register
    /// <c>AddRedisFusionCache</c> first when running more than one BFF replica.
    /// </summary>
    public static IServiceCollection AddModulusBff(this IServiceCollection services, IConfiguration configuration, Action<BffBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(configure);

        var section = configuration.GetSection(BffOptions.SectionName);
        services.AddOptions<BffOptions>().Bind(section).PostConfigure(o =>
        {
            if (o.GetServiceAddress(BffOptions.DefaultService) is null && configuration["Api:BaseUrl"] is { Length: > 0 } apiBaseUrl)
                o.Services[BffOptions.DefaultService] = new BffServiceOptions { Address = apiBaseUrl };
        });

        services.TryAddSingleton(TimeProvider.System);
        services.AddHttpContextAccessor();
        services.AddDataProtection();
        if (!services.Any(d => d.ServiceType == typeof(ICacheService)))
            services.AddModulusFusionCache(configuration);

        services.AddHttpClient(BffDefaults.AuthorityHttpClient, http => http.Timeout = TimeSpan.FromSeconds(30));
        services.TryAddSingleton<IBffDiscovery, BffDiscovery>();
        services.TryAddSingleton<IBffTokenClient, BffTokenClient>();
        services.TryAddSingleton<IUserTokenStore, ServerSideUserTokenStore>();
        services.TryAddSingleton<CookieUserTokenStore>();
        services.TryAddSingleton<IBffAccessTokenService, BffAccessTokenService>();
        services.TryAddSingleton<IBffClientContext, BffClientContext>();
        services.TryAddTransient<UserAccessTokenHandler>();
        services.TryAddTransient<BffClientHeaderHandler>();
        services.TryAddTransient<BffTenantHeaderHandler>();
        services.TryAddScoped<BffComposer>();
        services.TryAddScoped<IBffSessionService, BffSessionService>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAuthorizationHandler, BffClientRequirementHandler>());

        var registry = new BffClientRegistry();
        services.AddSingleton(registry);
        services.AddAuthorization();
        services.AddRateLimiter(o => o.RejectionStatusCode = StatusCodes.Status429TooManyRequests);

        var proxy = services.AddReverseProxy().AddTransforms<BffProxyTransformProvider>();
        services.AddSingleton<IProxyConfigProvider, BffProxyConfigProvider>();
        if (section.GetValue<bool>(nameof(BffOptions.UseServiceDiscovery)))
        {
            services.AddSingleton<BffServiceDiscoveryMarker>();
            services.AddServiceDiscovery();
            proxy.AddServiceDiscoveryDestinationResolver();
        }

        var builder = new BffBuilder(services, configuration, registry, services.AddAuthentication());
        configure(builder);
        if (builder.DefaultClient is { } defaultClient && !registry.Names.Contains(defaultClient, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException($"The default BFF client '{defaultClient}' is not registered; add it in AddModulusBff.");
        if (builder.OpenApiEnabled)
        {
            foreach (var name in registry.Names)
                services.AddModulusBffOpenApi(configuration, name);
        }

        return services;
    }

    private static void AddModulusBffOpenApi(this IServiceCollection services, IConfiguration configuration, string client)
#if NET9_0_OR_GREATER
        => Modulus.AspNetCore.OpenApi.OpenApiExtensions.AddModulusOpenApiDocument(services, configuration, client, openApi =>
            openApi.ShouldInclude = description =>
                description.ActionDescriptor.EndpointMetadata.OfType<BffClientMetadata>().Any(m => string.Equals(m.Name, client, StringComparison.OrdinalIgnoreCase)));
#else
        => throw new NotSupportedException("BFF OpenAPI documents need net9.0 or later (Microsoft.AspNetCore.OpenApi).");
#endif
}

/// <summary>Registers BFF clients. Each binds <c>Bff:Clients:{name}</c>.</summary>
public sealed class BffBuilder
{
    private readonly IConfiguration _configuration;
    private readonly BffClientRegistry _registry;
    private readonly AuthenticationBuilder _authentication;

    internal BffBuilder(IServiceCollection services, IConfiguration configuration, BffClientRegistry registry, AuthenticationBuilder authentication)
        => (Services, _configuration, _registry, _authentication) = (services, configuration, registry, authentication);

    public IServiceCollection Services { get; }

    internal bool OpenApiEnabled { get; private set; }

    /// <summary>A browser client: cookie session, server-side tokens, CSRF header, OIDC or password login.</summary>
    public BffBuilder AddWebClient(string name = "web", Action<BffClientOptions>? configure = null)
        => AddClient(name, BffClientKind.Web, configure);

    /// <summary>A native app: bearer tokens (JWT or introspection), app-version gate, device rate limit, ETags.</summary>
    public BffBuilder AddMobileClient(string name = "mobile", Action<BffClientOptions>? configure = null)
        => AddClient(name, BffClientKind.Mobile, configure);

    /// <summary>A machine-to-machine caller: client-credentials bearer tokens, idempotency keys required.</summary>
    public BffBuilder AddPartnerClient(string name = "partner", Action<BffClientOptions>? configure = null)
        => AddClient(name, BffClientKind.Partner, configure);

    /// <summary>
    /// Makes this host one client outside BFF endpoints too: a server-rendered web host (Razor Pages, MVC) whose pages
    /// use client <paramref name="name"/>'s session. Its scheme becomes the default authentication scheme, and typed
    /// clients (<see cref="BffHttpClientExtensions.AddBffUserAccessToken"/>) carry its session token from any page.
    /// Set <see cref="BffClientOptions.LoginPath"/> so unauthenticated page requests go to the sign-in page.
    /// </summary>
    public BffBuilder SetDefaultClient(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        DefaultClient = name;
        Services.AddSingleton(new BffDefaultClient(name));
        Services.Configure<AuthenticationOptions>(o =>
        {
            o.DefaultScheme = BffDefaults.Scheme(name);
            o.DefaultAuthenticateScheme = BffDefaults.Scheme(name);
            o.DefaultChallengeScheme = BffDefaults.Scheme(name);
            o.DefaultForbidScheme = BffDefaults.Scheme(name);
            o.DefaultSignInScheme = BffDefaults.Scheme(name);
            o.DefaultSignOutScheme = BffDefaults.Scheme(name);
        });
        return this;
    }

    internal string? DefaultClient { get; private set; }

    /// <summary>Publishes one OpenAPI document per client at <c>/openapi/{client}.json</c> (map with <c>MapOpenApi()</c>).</summary>
    public BffBuilder AddOpenApi()
    {
        OpenApiEnabled = true;
        return this;
    }

    /// <summary>Registers a client of <paramref name="kind"/>.</summary>
    public BffBuilder AddClient(string name, BffClientKind kind, Action<BffClientOptions>? configure = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        _registry.Add(name);

        var section = _configuration.GetSection($"{BffOptions.SectionName}:Clients:{name}");
        var optionsBuilder = Services.AddOptions<BffClientOptions>(name).Bind(section).Configure(o => o.Kind = kind);
        if (configure is not null)
            optionsBuilder.Configure(configure);

        // Scheme handler types are fixed at registration, so read the settings that pick them now.
        var snapshot = new BffClientOptions();
        section.Bind(snapshot);
        snapshot.Kind = kind;
        configure?.Invoke(snapshot);

        var scheme = BffDefaults.Scheme(name);
        if (kind == BffClientKind.Web)
            AddWebAuthentication(name, scheme, snapshot);
        else if (snapshot.TokenValidation == BffTokenValidation.Introspection)
            _authentication.AddScheme<BffIntrospectionOptions, BffIntrospectionHandler>(scheme, o => o.Client = name);
        else
            AddJwtAuthentication(scheme, name);

        Services.Configure<AuthorizationOptions>(o => o.AddPolicy(BffDefaults.Policy(name), policy => policy
            .AddAuthenticationSchemes(scheme)
            .RequireAuthenticatedUser()
            .AddRequirements(new BffClientRequirement(name))));

        Services.AddOptions<RateLimiterOptions>().Configure<IOptionsMonitor<BffClientOptions>>((o, clients) =>
            o.AddPolicy(BffDefaults.RateLimitPolicy(name), context => Partition(context, clients.Get(name))));

        return this;
    }

    private void AddJwtAuthentication(string scheme, string name)
    {
        _authentication.AddJwtBearer(scheme, _ => { });
        Services.AddOptions<JwtBearerOptions>(scheme).Configure<IOptionsMonitor<BffClientOptions>, IOptions<BffOptions>>((o, clients, bff) =>
        {
            var client = clients.Get(name);
            o.Authority = bff.Value.ResolveAuthority();
            o.RequireHttpsMetadata = bff.Value.RequireHttpsMetadata;
            o.MapInboundClaims = false;
            o.TokenValidationParameters.NameClaimType = BffClaims.Name;
            o.TokenValidationParameters.RoleClaimType = BffClaims.Role;
            o.TokenValidationParameters.ValidateAudience = client.Audiences.Count > 0;
            o.TokenValidationParameters.ValidAudiences = client.Audiences;
            var roleTypes = BffClaims.DefaultRoleClaimTypes(bff.Value.AuthServer).Concat(bff.Value.RoleClaimTypes).ToArray();
            o.Events = new JwtBearerEvents
            {
                OnTokenValidated = context =>
                {
                    if (context.Principal is { } principal)
                        context.Principal = BffClaims.Normalize(principal, roleTypes, scheme);
                    return Task.CompletedTask;
                },
            };
        });
    }

    private void AddWebAuthentication(string name, string scheme, BffClientOptions snapshot)
    {
        _authentication.AddCookie(scheme, _ => { });
        Services.AddOptions<CookieAuthenticationOptions>(scheme).Configure<IOptionsMonitor<BffClientOptions>>((o, clients) =>
        {
            var client = clients.Get(name);
            o.Cookie.Name = ".bff." + name;
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Lax;
            o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            o.ExpireTimeSpan = client.SessionLifetime;
            o.SlidingExpiration = true;
            if (client.LoginPath is { Length: > 0 } loginPath)
                o.LoginPath = loginPath;
            if (client.AccessDeniedPath is { Length: > 0 } deniedPath)
                o.AccessDeniedPath = deniedPath;
            o.Events = new CookieAuthenticationEvents
            {
                // Pages of a server-rendered host go to the sign-in page; BFF endpoints (called by script) get a status.
                OnRedirectToLogin = c => client.LoginPath is { Length: > 0 } && !IsBffEndpoint(c.HttpContext)
                    ? Redirect(c.Response, c.RedirectUri)
                    : Status(c.Response, StatusCodes.Status401Unauthorized),
                OnRedirectToAccessDenied = c => client.AccessDeniedPath is { Length: > 0 } && !IsBffEndpoint(c.HttpContext)
                    ? Redirect(c.Response, c.RedirectUri)
                    : Status(c.Response, StatusCodes.Status403Forbidden),
                OnValidatePrincipal = async c =>
                {
                    // A server-side session whose tokens were evicted (logout elsewhere, cache flush) is over.
                    if (c.Principal is null || client.TokenStorage == BffTokenStorage.Cookie)
                        return;
                    var store = c.HttpContext.RequestServices.GetRequiredService<IUserTokenStore>();
                    if (await store.GetAsync(name, c.Principal, c.Properties, c.HttpContext.RequestAborted).ConfigureAwait(false) is null)
                    {
                        c.RejectPrincipal();
                        await c.HttpContext.SignOutAsync(scheme).ConfigureAwait(false);
                    }
                },
            };
        });

        if (snapshot.LoginMode != BffLoginMode.Oidc)
            return;

        var oidcScheme = BffDefaults.OidcScheme(name);
        _authentication.AddOpenIdConnect(oidcScheme, _ => { });
        Services.AddOptions<OpenIdConnectOptions>(oidcScheme).Configure<IOptionsMonitor<BffClientOptions>, IOptions<BffOptions>>((o, clients, bff) =>
        {
            var client = clients.Get(name);
            var prefix = BffPaths.Normalize(client.PathPrefix);
            o.Authority = bff.Value.ResolveAuthority();
            o.RequireHttpsMetadata = bff.Value.RequireHttpsMetadata;
            o.ClientId = client.ClientId;
            o.ClientSecret = client.ClientSecret;
            o.ResponseType = "code";
            o.UsePkce = true;
            o.SaveTokens = true;
            o.MapInboundClaims = false;
            // Entra ID's userinfo is Microsoft Graph and adds nothing the id token lacks.
            o.GetClaimsFromUserInfoEndpoint = bff.Value.AuthServer != BffAuthServer.AzureAd;
            o.ClaimActions.MapAllExcept("aud", "iss", "iat", "nbf", "exp", "nonce", "at_hash", "c_hash", "auth_time");
            o.CallbackPath = prefix + "/signin-oidc";
            o.SignedOutCallbackPath = prefix + "/signout-callback-oidc";
            o.SignInScheme = scheme;
            o.TokenValidationParameters.NameClaimType = BffClaims.Name;
            o.TokenValidationParameters.RoleClaimType = BffClaims.Role;
            o.Scope.Clear();
            foreach (var s in client.Scopes)
                o.Scope.Add(s);

            var roleTypes = BffClaims.DefaultRoleClaimTypes(bff.Value.AuthServer).Concat(bff.Value.RoleClaimTypes).ToArray();
            o.Events = new OpenIdConnectEvents
            {
                OnRedirectToIdentityProvider = c =>
                {
                    foreach (var (key, value) in client.AuthorizationParameters)
                        c.ProtocolMessage.SetParameter(key, value);
                    return Task.CompletedTask;
                },
                OnTicketReceived = c => OnWebTicketReceivedAsync(c, name, scheme, roleTypes),
            };
        });
    }

    /// <summary>Normalizes the claims, opens the session (<c>bff_sid</c>) and moves the tokens to the store.</summary>
    private static async Task OnWebTicketReceivedAsync(TicketReceivedContext context, string name, string scheme, string[] roleTypes)
    {
        if (context.Principal is null || context.Properties is null)
            return;

        var principal = BffClaims.Normalize(context.Principal, roleTypes, scheme);
        ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim(BffDefaults.SessionIdClaim, Guid.NewGuid().ToString("N")));
        context.Principal = principal;

        var properties = context.Properties;
        var access = properties.GetTokenValue("access_token");
        if (access is null)
            return;
        var expires = DateTimeOffset.TryParse(properties.GetTokenValue("expires_at"), System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : DateTimeOffset.UtcNow.AddMinutes(5);
        var tokens = new BffUserTokens(access, properties.GetTokenValue("refresh_token"), expires, properties.GetTokenValue("id_token"));

        var services = context.HttpContext.RequestServices;
        var client = services.GetRequiredService<IOptionsMonitor<BffClientOptions>>().Get(name);
        properties.StoreTokens([]);
        IUserTokenStore store = client.TokenStorage == BffTokenStorage.Cookie
            ? services.GetRequiredService<CookieUserTokenStore>()
            : services.GetRequiredService<IUserTokenStore>();
        await store.StoreAsync(name, principal, properties, tokens, context.HttpContext.RequestAborted).ConfigureAwait(false);
    }

    private static RateLimitPartition<string> Partition(HttpContext context, BffClientOptions client)
    {
        if (client.RateLimit is not { PermitLimit: > 0 } limit)
            return RateLimitPartition.GetNoLimiter("none");

        // The partition must come from something the caller cannot choose. A header such as X-Device-Id is picked by the
        // client, so keying on it let anyone escape the limit (and grow the limiter table) by sending a new value per request.
        var user = context.User;
        var key = client.Kind switch
        {
            BffClientKind.Partner => BffClaims.GetClientId(user) is { } clientId ? "client:" + clientId : null,
            _ => Subject(user) ?? (BffClaims.GetClientId(user) is { } app ? "client:" + app : null),
        } ?? "ip:" + context.Connection.RemoteIpAddress;

        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = limit.PermitLimit,
            Window = limit.Window,
            QueueLimit = limit.QueueLimit,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        });

        static string? Subject(ClaimsPrincipal user) => user.FindFirst("sub")?.Value is { } sub ? "user:" + sub : null;
    }

    private static Task Status(HttpResponse response, int status)
    {
        response.StatusCode = status;
        return Task.CompletedTask;
    }

    private static Task Redirect(HttpResponse response, string uri)
    {
        response.Redirect(uri);
        return Task.CompletedTask;
    }

    private static bool IsBffEndpoint(HttpContext context)
        => context.GetEndpoint()?.Metadata.GetMetadata<BffClientMetadata>() is not null;
}
