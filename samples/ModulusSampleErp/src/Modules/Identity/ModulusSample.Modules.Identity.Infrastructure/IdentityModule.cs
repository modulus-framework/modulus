using Modulus.Identity.Abstractions;
using Modulus.Identity.EntityFrameworkCore.Extensions;
using Modulus.Identity.Extensions;
using ModulusSample.Modules.Identity.Application.Abstractions.Authentication;
using ModulusSample.Modules.Identity.Application.Abstractions.Identity;
using ModulusSample.Modules.Identity.Application.IntegrationEvents;
using ModulusSample.Modules.Identity.Presentation;
using ModulusSample.Shared.Application.Abstractions.Oidc;
using ModulusSample.Shared.Application.Authorization;
using ModulusSample.Shared.Application.Caching;
using ModulusSample.Shared.Infrastructure.Authentication;
using Modulus.Events.Abstractions;
using ModulusSample.Modules.Identity.Domain.Repositories;
using ModulusSample.Modules.Identity.Infrastructure.Authentication;
using ModulusSample.Modules.Identity.Infrastructure.Caching;
using ModulusSample.Modules.Identity.Infrastructure.Configurations;
using ModulusSample.Modules.Identity.Infrastructure.Database;
using ModulusSample.Modules.Identity.Infrastructure.OpenIddict;
using ModulusSample.Modules.Identity.Infrastructure.Repositories;
using ModulusSample.Modules.Identity.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using FluentValidation;
using ModulusSample.Modules.Identity.Application.Abstractions;
using ModulusSample.Modules.Identity.Application.Abstractions.Data;
using ModulusSample.Shared.Application.Abstractions;
using ModulusSample.Shared.Application.Data;
using ModulusSample.Shared.Infrastructure.Caching;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Modulus.Core.Abstractions;
using Modulus.Authorization.Extensions;

using Modulus.Mediator.Extensions;
using ModulusSample.Modules.Identity.Application;
namespace ModulusSample.Modules.Identity.Infrastructure;

public sealed class IdentityModule : ModulusModule
{
    public override void ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        AddPermissions(services);
        services.AddMediatorHandlers(typeof(NotifyUserCreatedHandler).Assembly);
        services.AddValidatorsFromAssembly(Application.AssemblyReference.Assembly);
        AddDomainEventHandlers(services);
        AddIntegrationEventHandlers(services);
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

    public static Type[] HandledIntegrationEvents =>
    [
        typeof(UserProfileUpdatedIntegrationEvent),
    ];

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
                .UseSnakeCaseNamingConvention());

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

    #region Domain Event Handlers

    private static void AddDomainEventHandlers(IServiceCollection services)
    {
        Type openHandlerType = typeof(Modulus.Events.Abstractions.IDomainEventHandler<>);
        Type[] domainEventHandlers = Application.AssemblyReference.Assembly
            .GetTypes()
            .Where(t => !t.IsAbstract && !t.IsInterface && t.GetInterfaces().Any(i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == openHandlerType))
            .ToArray();

        foreach (Type domainEventHandler in domainEventHandlers)
        {
            services.TryAddScoped(domainEventHandler);
        }
    }

    #endregion

    #region Integration Event Handlers

    private static void AddIntegrationEventHandlers(IServiceCollection services)
    {
        Type openHandlerType = typeof(Modulus.Events.Abstractions.IIntegrationEventHandler<>);
        Type[] integrationEventHandlers = Presentation.AssemblyReference.Assembly
            .GetTypes()
            .Where(t => !t.IsAbstract && !t.IsInterface && t.GetInterfaces().Any(i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == openHandlerType))
            .ToArray();

        foreach (Type integrationEventHandler in integrationEventHandlers)
        {
            services.TryAddScoped(integrationEventHandler);
        }
    }

    #endregion
}
