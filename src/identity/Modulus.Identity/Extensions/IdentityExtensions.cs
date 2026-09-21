namespace Modulus.Identity.Extensions;

using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Modulus.Core.Abstractions;
using Modulus.Core.Null;
using Modulus.Identity;
using Modulus.Identity.Abstractions;
using Modulus.Identity.Guards;
using OpenIddict.Abstractions;

public static class IdentityExtensions
{
    /// <summary>
    /// Registers ASP.NET Core Identity with Modulus user/role types,
    /// the ClaimsPrincipalCurrentUser adapter, and cookie auth.
    /// </summary>
    public static IdentityBuilder AddModulusIdentity<TContext, TUser, TRole>(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IdentityOptions>? configureIdentity = null)
        where TContext : Microsoft.EntityFrameworkCore.DbContext
        where TUser : ModulusUser, new()
        where TRole : ModulusRole, new()
    {
        services.Configure<ModulusIdentityOptions>(
            configuration.GetSection("Identity"));

        // Read the bound options to apply Identity settings that cannot be
        // deferred until IOptions resolution (they are needed at registration).
        var identityOptions = configuration.GetSection("Identity")
            .Get<ModulusIdentityOptions>() ?? new ModulusIdentityOptions();

        services.AddHttpContextAccessor();
        UseClaimsPrincipalCurrentUser(services);

        // Token delivery for the account endpoints (password reset / email
        // confirmation). No-op by default: without a real sender the emails
        // are discarded, keeping the flows unusable (fail-closed) rather than
        // returning tokens in API responses.
        services.TryAddScoped<IIdentityEmailSender, NoopIdentityEmailSender>();

        // Make the closed-generic AccountController<TUser> discoverable by
        // MVC — the default controller feature provider rejects generic
        // controller types, leaving every /account/* route unreachable.
        services.AddControllers().ConfigureApplicationPartManager(manager =>
            manager.FeatureProviders.Add(
                new AccountControllerFeatureProvider(typeof(TUser))));

        // Register the concrete user type in this host's container so the
        // token controller can resolve UserManager<TConcreteUser> at runtime
        // without knowing the generic parameter at compile time. A singleton
        // descriptor (not a process-wide static) so parallel in-process hosts
        // keep their own user type.
        services.TryAddSingleton(new ModulusUserTypeDescriptor(typeof(TUser)));

        var builder = services.AddIdentity<TUser, TRole>(options =>
        {
            options.SignIn.RequireConfirmedEmail = identityOptions.RequireConfirmedEmail;
            options.Password.RequireDigit = true;
            options.Password.RequiredLength = 8;
            options.Password.RequireNonAlphanumeric = false;
            options.Password.RequireUppercase = true;
            options.User.RequireUniqueEmail = true;
            configureIdentity?.Invoke(options);
        });

        builder.AddEntityFrameworkStores<TContext>()
               .AddDefaultTokenProviders();

        // Replace the deny-default validator (registered by AddModulusOpenIddict)
        // with the SignInManager-backed implementation so the password grant
        // actually verifies credentials.
        services.AddScoped(
            typeof(IPasswordGrantCredentialValidator),
            typeof(IdentityPasswordGrantValidator<>).MakeGenericType(typeof(TUser)));

        services.ConfigureApplicationCookie(options =>
        {
            options.LoginPath = "/account/login";
            options.LogoutPath = "/account/logout";
            options.ExpireTimeSpan = TimeSpan.FromHours(1);
            options.SlidingExpiration = true;

            // Never let the auth cookie cross the wire over plain HTTP —
            // SameAsRequest (the ASP.NET default) silently does exactly that
            // on a misconfigured HTTP deployment. Local dev should use the
            // default https localhost bindings (or a scoped override).
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
        });

        return builder;
    }

    /// <summary>
    /// Registers OpenIddict server (authorization code + refresh token).
    /// The EF Core store must be configured separately via AddModulusIdentityStore.
    /// Also registers a deny-by-default <see cref="IPasswordGrantCredentialValidator"/>
    /// so the token endpoint cannot mint tokens without a credential check until
    /// <see cref="AddModulusIdentity{TContext, TUser, TRole}"/> replaces it.
    /// </summary>
    public static IServiceCollection AddModulusOpenIddict(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<OpenIddictServerBuilder>? configure = null)
    {
        // Fail-closed: no password grant succeeds unless AddModulusIdentity
        // (or a custom validator) overrides this default. TryAdd, so the
        // outcome does not depend on call order: AddModulusIdentity registered
        // first (e.g. from a module's ConfigureServices, which runs inside
        // AddModulus) is not clobbered by a later AddModulusOpenIddict.
        services.TryAddScoped<
            IPasswordGrantCredentialValidator,
            NullPasswordGrantCredentialValidator>();

        // Enforce the single-external-provider invariant at startup. The guard
        // is a no-op when zero or one provider is registered, so double-register
        // it (e.g. when AddModulusOpenIddict is mistakenly called twice) is safe.
        services.AddHostedService<SingleExternalProviderGuard>();

        // Fail fast if UseDevelopmentCertificates is set in Production — see
        // DevelopmentCertificateGuard's doc comment for why this exists.
        services.AddHostedService<DevelopmentCertificateGuard>();

        var identityOptions = configuration.GetSection("Identity")
            .Get<ModulusIdentityOptions>() ?? new ModulusIdentityOptions();

        services.AddOpenIddict()
            .AddServer(options =>
            {
                // Token storage is required for revocation (RFC 7009): the server
                // must be able to look up and mark tokens as revoked. The EF Core
                // store is configured by AddModulusIdentityStore.
                options.SetTokenEndpointUris("/connect/token")
                       .SetAuthorizationEndpointUris("/connect/authorize")
                       .SetUserInfoEndpointUris("/connect/userinfo")
                       .SetRevocationEndpointUris("/connect/revoke");

                // The refresh grant is gated on Identity:EnableRefreshToken
                // (default true). Setting it to false makes the token endpoint
                // reject grant_type=refresh_token requests.
                if (identityOptions.EnableRefreshToken)
                    options.AllowRefreshTokenFlow();

                // Authorization code flow: ModulusAuthorizeController serves
                // /connect/authorize and ModulusTokenController redeems the code.
                // Off by default; enable via Identity:AllowAuthorizationCodeFlow.
                // PKCE is mandatory (a public client such as a mobile or desktop
                // app cannot keep a secret, so the code is bound to a verifier
                // only the app that started the flow knows).
                if (identityOptions.AllowAuthorizationCodeFlow)
                {
                    options.AllowAuthorizationCodeFlow()
                           .RequireProofKeyForCodeExchange();
                }

                // ROPC is off by default (removed in OAuth 2.1). Opt in only for
                // trusted first-party clients via Identity:AllowPasswordFlow.
                if (identityOptions.AllowPasswordFlow)
                    options.AllowPasswordFlow();

                // Must include the scopes the token endpoint's allow-list can
                // grant: without `openid` every scope request is rejected with
                // invalid_scope, and without `offline_access` OpenIddict never
                // mints a refresh token even though the refresh flow is enabled.
                options.RegisterScopes(
                    OpenIddictConstants.Scopes.OpenId,
                    OpenIddictConstants.Scopes.Email,
                    OpenIddictConstants.Scopes.Profile,
                    OpenIddictConstants.Scopes.Roles,
                    OpenIddictConstants.Scopes.OfflineAccess,
                    "modulus");

                // Apply token lifetimes from the bound identity options.
                options.SetAccessTokenLifetime(
                        TimeSpan.FromMinutes(identityOptions.AccessTokenLifetimeMin))
                       .SetRefreshTokenLifetime(
                        TimeSpan.FromDays(identityOptions.RefreshTokenLifetimeDays));

                // Development certificates are ephemeral (regenerated per restart)
                // and must never sign production tokens. Off by default; enable via
                // Identity:UseDevelopmentCertificates in Development only. In
                // production register real certificates through the configure
                // callback below — otherwise OpenIddict fails fast at startup.
                if (identityOptions.UseDevelopmentCertificates)
                {
                    options.AddDevelopmentEncryptionCertificate()
                           .AddDevelopmentSigningCertificate();
                }

                options.UseAspNetCore()
                       .EnableTokenEndpointPassthrough()
                       .EnableAuthorizationEndpointPassthrough()
                       .EnableUserInfoEndpointPassthrough();

                configure?.Invoke(options);
            })
            .AddValidation(options =>
            {
                options.UseLocalServer();
                options.UseAspNetCore();
            });

        return services;
    }

    /// <summary>
    /// Makes <see cref="ClaimsPrincipalCurrentUser"/> the <see cref="ICurrentUser"/> unless the app registered its own. Several Modulus
    /// packages <c>TryAdd</c> the fail-closed <see cref="NullCurrentUser"/> as a default (<c>AddModulus</c>, <c>AddMediator</c>,
    /// <c>AddModulusUi</c>, ...), and one registered first made a plain <c>TryAdd</c> here a no-op, so a signed-in administrator was still
    /// anonymous to every <c>ICurrentUser</c> consumer (menu permission filtering, entity-field permissions, audit). Only that default is
    /// replaced; a custom implementation registered earlier is kept.
    /// </summary>
    private static void UseClaimsPrincipalCurrentUser(IServiceCollection services)
    {
        var registered = services.LastOrDefault(d => d.ServiceType == typeof(ICurrentUser));
        if (registered is null || registered.ImplementationType == typeof(NullCurrentUser))
        {
            services.Replace(ServiceDescriptor.Scoped<ICurrentUser, ClaimsPrincipalCurrentUser>());
        }
    }

    /// <summary>
    /// Routes <see cref="ICurrentUser.HasPermission"/> through the server-side grant
    /// store: the current principal's effective permissions are resolved from its
    /// user id and role claims (via <c>IPermissionResolver</c>) instead of from
    /// fine-grained "permission" claims on the token. Requires the authorization
    /// services (<c>AddModulusAuthorization</c>) and, for any permissions to resolve,
    /// grants seeded via <c>AddPermissionGrants</c>. Opt-in — without it,
    /// <see cref="ClaimsPrincipalCurrentUser"/> falls back to permission claims.
    /// </summary>
    public static IServiceCollection AddGrantStorePermissionChecker(
        this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<IPermissionChecker, GrantStorePermissionChecker>();
        return services;
    }

    /// <summary>
    /// Registers identity seed roles and the identity seeder for the default
    /// <see cref="ModulusUser"/>/<see cref="ModulusRole"/> types.
    /// </summary>
    public static IServiceCollection AddIdentitySeeder(
        this IServiceCollection services,
        params ModulusRoleSeed[] roleSeeds)
        => services.AddIdentitySeeder<ModulusUser, ModulusRole>(roleSeeds);

    /// <summary>
    /// Registers identity seed roles and the identity seeder for the given
    /// user/role types.
    /// </summary>
    public static IServiceCollection AddIdentitySeeder<TUser, TRole>(
        this IServiceCollection services,
        params ModulusRoleSeed[] roleSeeds)
        where TUser : ModulusUser, new()
        where TRole : ModulusRole, new()
    {
        services.AddSingleton(roleSeeds);
        services.AddScoped<IIdentitySeeder>(sp =>
            (IIdentitySeeder)ActivatorUtilities.CreateInstance(
                sp,
                typeof(DefaultIdentitySeeder<,>)
                    .MakeGenericType(
                        typeof(TUser),
                        typeof(TRole)),
                sp.GetRequiredService<IEnumerable<ModulusRoleSeed>>())!);
        return services;
    }
}
