using Modulus.Identity.Abstractions;
using Modulus.Identity.EntityFrameworkCore.Extensions;
using Modulus.Identity.Extensions;
using TradeFlow.Modules.Identity.Application.Abstractions.Authentication;
using TradeFlow.Modules.Identity.Application.Abstractions.Identity;
using TradeFlow.Modules.Identity.Presentation;
using TradeFlow.Shared.Application.Abstractions.Oidc;
using TradeFlow.Shared.Application.Authorization;
using TradeFlow.Shared.Application.Caching;
using TradeFlow.Shared.Infrastructure.Authentication;
using Modulus.Events.Abstractions;
using TradeFlow.Modules.Identity.Domain.Repositories;
using TradeFlow.Modules.Identity.Infrastructure.Authentication;
using TradeFlow.Modules.Identity.Infrastructure.Caching;
using TradeFlow.Modules.Identity.Infrastructure.Configurations;
using TradeFlow.Modules.Identity.Infrastructure.Database;
using TradeFlow.Modules.Identity.Infrastructure.OpenIddict;
using TradeFlow.Modules.Identity.Infrastructure.Repositories;
using TradeFlow.Modules.Identity.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using FluentValidation;
using TradeFlow.Modules.Identity.Application.Abstractions;
using TradeFlow.Shared.Application.Abstractions;
using TradeFlow.Shared.Application.Data;
using TradeFlow.Shared.Infrastructure.Caching;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Modulus.Core.Abstractions;
using Modulus.Authorization.Extensions;

using Modulus.Mediator.Extensions;
using TradeFlow.Modules.Identity.Application;
namespace TradeFlow.Modules.Identity.Infrastructure;

public sealed class IdentityModule : ModulusModule
{
    public override void ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        AddPermissions(services);
        services.AddMediatorHandlers(typeof(NotifyUserCreatedHandler).Assembly);
        services.AddValidatorsFromAssembly(Application.AssemblyReference.Assembly);
        AddInfrastructure(services, configuration);
    }

    private static void AddPermissions(IServiceCollection services)
    {
        services.AddPermissions("Identity", registry =>
        {
            registry.Add(IdentityPermissions.IdentityProfileViewOwn, "View own profile");
            registry.Add(IdentityPermissions.IdentityProfileManageOwn, "Manage own profile");
            registry.Add(IdentityPermissions.IdentityPasswordChangeOwn, "Change own password");
            registry.Add(IdentityPermissions.IdentityUserViewAll, "View all users");
            registry.Add(IdentityPermissions.IdentityUserManageAll, "Manage all users");
            registry.Add(IdentityPermissions.IdentityRoleManageAll, "Manage all roles");
            registry.Add(IdentityPermissions.IdentityAdmin, "Administrator access");
        });
    }

    private static void AddInfrastructure(
        IServiceCollection services,
        IConfiguration configuration)
    {
        // ============================================
        // DATABASE CONFIGURATION
        // ============================================
        services.AddDbContext<IdentityDbContext>((sp, options) =>
            options
                .UseNpgsql(
                    configuration.GetConnectionString("Database"),
                    npgsqlOptions => npgsqlOptions
                        .MigrationsHistoryTable(HistoryRepository.DefaultTableName, Schemas.Users)
                        .UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
                .UseSnakeCaseNamingConvention()
                // OpenIddict's model conventions (UseOpenIddict) resolve differently at
                // design time (dotnet-ef, no OpenIddict core registered) than at runtime,
                // so the runtime model legitimately diverges from the design-time
                // snapshot. Downgrade EF's pending-changes migration guard to a log for
                // this context only — the schema itself is migration-managed.
                .ConfigureWarnings(w => w.Log(RelationalEventId.PendingModelChangesWarning)));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<IdentityDbContext>());

        // ============================================
        // SHARED INFRASTRUCTURE BRIDGE
        // (abstractions consumed by the Dapper read-side + auth pipeline)
        // ============================================
        services.AddScoped<IDbConnectionFactory, PostgresDbConnectionFactory>();
        services.AddScoped<IUserIdentifierMapper, UserIdentifierMapper>();
        services.AddMemoryCache();
        services.TryAddSingleton<ICacheService, MemoryCacheService>();

        // ============================================
        // APPLICATION SERVICES
        // ============================================
        services.AddDistributedMemoryCache();
        services.AddScoped<IUserContext, UserContext>();
        services.AddScoped<ICurrentUser, CurrentUserAccessor>();

        // Identity services
        services.AddScoped<IUserIdentityService, UserIdentityService>();
        services.AddScoped<IPasswordHasher, PasswordHasher>();

        // ============================================
        // REPOSITORIES
        // ============================================
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<IPermissionRepository, PermissionRepository>();
        services.AddScoped<IDeviceTokenRepository, DeviceTokenRepository>();
        services.AddScoped<IEmailVerificationTokenRepository, EmailVerificationTokenRepository>();
        services.AddScoped<IUserSessionRepository, UserSessionRepository>();

        services.AddScoped<IPermissionService, PermissionService>();
        services.AddScoped<IEmailVerificationTokenService, EmailVerificationTokenService>();
        services.AddScoped<IRateLimitingService, RateLimitingService>();
        services.AddScoped<ISessionService, SessionService>();

        // OpenIddict password grant validator (replaces deny-by-default NullPasswordGrantCredentialValidator)
        services.AddScoped<IPasswordGrantCredentialValidator, SamplePasswordGrantValidator>();

        // Seed OpenIddict client applications on startup
        services.Configure<OpenIddictClientOptions>(
            configuration.GetSection("Identity:Clients"));
        services.AddHostedService<OpenIddictClientSeeder>();
    }
}
