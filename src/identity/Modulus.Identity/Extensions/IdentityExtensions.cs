namespace Modulus.Identity.Extensions;

using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Modulus.Core.Abstractions;
using Modulus.Identity;
using Modulus.Identity.Abstractions;
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
        services.AddScoped<ICurrentUser, ClaimsPrincipalCurrentUser>();

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

        var identityOptions = configuration.GetSection("Identity")
            .Get<ModulusIdentityOptions>() ?? new ModulusIdentityOptions();

        services.AddOpenIddict()
            .AddServer(options =>
            {
                options.SetTokenEndpointUris("/connect/token")
                       .SetAuthorizationEndpointUris("/connect/authorize")
                       .SetUserInfoEndpointUris("/connect/userinfo");

                options.AllowAuthorizationCodeFlow()
                       .AllowRefreshTokenFlow()
                       .AllowPasswordFlow();

                options.RegisterScopes(
                    OpenIddictConstants.Scopes.Email,
                    OpenIddictConstants.Scopes.Profile,
                    OpenIddictConstants.Scopes.Roles,
                    "modulus");

                // Apply token lifetimes from the bound identity options.
                options.SetAccessTokenLifetime(
                        TimeSpan.FromMinutes(identityOptions.AccessTokenLifetimeMin))
                       .SetRefreshTokenLifetime(
                        TimeSpan.FromDays(identityOptions.RefreshTokenLifetimeDays));

                options.AddDevelopmentEncryptionCertificate()
                       .AddDevelopmentSigningCertificate();

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
