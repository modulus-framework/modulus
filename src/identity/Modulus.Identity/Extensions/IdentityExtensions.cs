namespace Modulus.Identity.Extensions;

using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Modulus.Core.Abstractions;
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
        services.TryAddScoped<ICurrentUser, ClaimsPrincipalCurrentUser>();

        // Store the concrete user type so the token controller can resolve
        // UserManager<TConcreteUser> at runtime without knowing the generic
        // parameter at compile time.  The controller currently resolves
        // UserManager<ModulusUser>, which returns null for derived user types
        // — this field bridges the gap.
        ModulusUserType.Value = typeof(TUser);

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
        // (or a custom validator) overrides this default.
        services.AddScoped<
            IPasswordGrantCredentialValidator,
            NullPasswordGrantCredentialValidator>();

        // Enforce the single-external-provider invariant at startup. The guard
        // is a no-op when zero or one provider is registered, so double-register
        // it (e.g. when AddModulusOpenIddict is mistakenly called twice) is safe.
        services.AddHostedService<SingleExternalProviderGuard>();

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

                options.AllowRefreshTokenFlow();

                // Authorization code flow requires the app to implement its
                // own connect/authorize endpoint — the framework only ships
                // the token endpoint. Off by default; enable via
                // Identity:AllowAuthorizationCodeFlow.
                if (identityOptions.AllowAuthorizationCodeFlow)
                    options.AllowAuthorizationCodeFlow();

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
