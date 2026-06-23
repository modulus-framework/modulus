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

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, ClaimsPrincipalCurrentUser>();

        var builder = services.AddIdentity<TUser, TRole>(options =>
        {
            options.SignIn.RequireConfirmedEmail = true;
            options.Password.RequireDigit = true;
            options.Password.RequiredLength = 8;
            options.Password.RequireNonAlphanumeric = false;
            options.Password.RequireUppercase = true;
            options.User.RequireUniqueEmail = true;
            configureIdentity?.Invoke(options);
        });

        builder.AddEntityFrameworkStores<TContext>()
               .AddDefaultTokenProviders();

        services.ConfigureApplicationCookie(options =>
        {
            options.LoginPath  = "/account/login";
            options.LogoutPath = "/account/logout";
            options.ExpireTimeSpan = TimeSpan.FromHours(1);
            options.SlidingExpiration = true;
        });

        return builder;
    }

    /// <summary>
    /// Registers OpenIddict server (authorization code + refresh token).
    /// The EF Core store must be configured separately via AddModulusIdentityStore.
    /// </summary>
    public static IServiceCollection AddModulusOpenIddict(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<OpenIddictServerBuilder>? configure = null)
    {
        services.AddOpenIddict()
            .AddServer(options =>
            {
                options.SetTokenEndpointUris("/connect/token")
                       .SetAuthorizationEndpointUris("/connect/authorize")
                       .SetUserInfoEndpointUris("/connect/userinfo");

                options.AllowAuthorizationCodeFlow()
                       .AllowRefreshTokenFlow();

                options.RegisterScopes(
                    OpenIddictConstants.Scopes.Email,
                    OpenIddictConstants.Scopes.Profile,
                    OpenIddictConstants.Scopes.Roles,
                    "modulus");

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
    /// Registers identity seed roles and the identity seeder.
    /// </summary>
    public static IServiceCollection AddIdentitySeeder(
        this IServiceCollection services,
        params ModulusRoleSeed[] roleSeeds)
    {
        services.AddSingleton(roleSeeds);
        services.AddScoped<IIdentitySeeder>(sp =>
            (IIdentitySeeder)ActivatorUtilities.CreateInstance(
                sp,
                typeof(DefaultIdentitySeeder<,>)
                    .MakeGenericType(
                        typeof(ModulusUser),
                        typeof(ModulusRole)),
                sp.GetRequiredService<IEnumerable<ModulusRoleSeed>>())!);
        return services;
    }
}
