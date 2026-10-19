namespace Modulus.MultiTenancy.EntityFrameworkCore;

using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Authorization.Extensions;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Security;
using Modulus.Core.Abstractions.Tenancy;

/// <summary>Body of <c>POST {tenantId}/{storeKey}</c>: the provider and the connection string to store as the secret.</summary>
/// <param name="ProviderKey">The provider, for example <c>postgresql</c>.</param>
/// <param name="ConnectionString">The connection string. It is written to the secret store and never returned.</param>
public sealed record ProvisionDataStoreRequest(string ProviderKey, string ConnectionString);

/// <summary>Body of <c>POST {tenantId}/{storeKey}/rotate</c>: the replacement connection string.</summary>
/// <param name="ConnectionString">The new connection string. It is written to the secret store and never returned.</param>
public sealed record RotateDataStoreRequest(string ConnectionString);

/// <summary>A tenant data store as the admin API reports it. It never carries the connection string.</summary>
/// <param name="TenantId">The tenant.</param>
/// <param name="StoreKey">The store.</param>
/// <param name="ProviderKey">The provider.</param>
/// <param name="Isolation">How the tenant's data is isolated.</param>
/// <param name="Status">The lifecycle state.</param>
/// <param name="SecretVersion">The secret version the store serves.</param>
public sealed record TenantDataStoreResponse(
    Guid TenantId,
    string StoreKey,
    string ProviderKey,
    string Isolation,
    string Status,
    int SecretVersion);

/// <summary>
/// The tenant data-store admin API (Phase 4): read a store, provision one, rotate its connection string. It is meant for the
/// provisioning host, which holds the writer identity; the runtime host does not map it. Every route needs
/// <see cref="ManagePermission"/>, which is Critical, so the step-up policy applies when it is on. The routes run in host
/// context, and a response never carries a connection string.
/// </summary>
public static partial class TenantDataStoreEndpoints
{
    /// <summary>The permission that guards the routes.</summary>
    public const string ManagePermission = "tenancy:data-stores:manage";

    private static readonly Regex StoreKeyPattern = StoreKeyRegex();

    /// <summary>Registers the permission. Requires <c>AddModulusAuthorization</c> for it to be enforced.</summary>
    public static IServiceCollection AddModulusTenantProvisioningAdmin(this IServiceCollection services)
        => services.AddPermissions("Modulus.Tenancy.DataStores", registry =>
            registry.Add(
                ManagePermission,
                "Provision and rotate the database connection of a tenant (the secret it reads, never shown).",
                null,
                PermissionSensitivity.Critical));

    /// <summary>Maps <c>{prefix}/{tenantId}/{storeKey}</c> (read, provision) and <c>.../rotate</c> (default prefix <c>/api/tenancy/data-stores</c>).</summary>
    public static RouteGroupBuilder MapModulusTenantProvisioning(this IEndpointRouteBuilder endpoints, string prefix = "/api/tenancy/data-stores")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var group = endpoints.MapGroup(prefix).RequireAuthorization(ManagePermission);

        group.MapGet("/{tenantId:guid}/{storeKey}", async (Guid tenantId, string storeKey, ITenantDataStoreCatalog catalog, ICurrentTenant tenant, CancellationToken ct) =>
        {
            if (!StoreKeyPattern.IsMatch(storeKey))
                return InvalidStoreKey();
            using var _ = tenant.Change(null);
            return await catalog.FindAsync(tenantId, storeKey, ct) is { } row
                ? Results.Ok(ToResponse(row))
                : Results.NotFound();
        });

        group.MapPost("/{tenantId:guid}/{storeKey}", async (
            Guid tenantId, string storeKey, ProvisionDataStoreRequest body, ITenantDataStoreProvisioner provisioner, ICurrentTenant tenant, CancellationToken ct) =>
        {
            if (!StoreKeyPattern.IsMatch(storeKey))
                return InvalidStoreKey();
            var problems = Validate(body.ProviderKey, body.ConnectionString);
            if (problems is not null)
                return problems;

            using var _ = tenant.Change(null);
            var row = await provisioner.ProvisionAsync(tenantId, storeKey, body.ProviderKey, body.ConnectionString, ct);
            return Results.Ok(ToResponse(row));
        });

        group.MapPost("/{tenantId:guid}/{storeKey}/rotate", async (
            Guid tenantId, string storeKey, RotateDataStoreRequest body, ITenantDataStoreProvisioner provisioner, ICurrentTenant tenant, CancellationToken ct) =>
        {
            if (!StoreKeyPattern.IsMatch(storeKey))
                return InvalidStoreKey();
            if (string.IsNullOrWhiteSpace(body.ConnectionString))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["connectionString"] = ["A connection string is required."] });

            using var _ = tenant.Change(null);
            var row = await provisioner.RotateAsync(tenantId, storeKey, body.ConnectionString, ct);
            return Results.Ok(ToResponse(row));
        });

        return group;
    }

    private static IResult? Validate(string providerKey, string connectionString)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(providerKey))
            errors["providerKey"] = ["A provider is required."];
        if (string.IsNullOrWhiteSpace(connectionString))
            errors["connectionString"] = ["A connection string is required."];
        return errors.Count == 0 ? null : Results.ValidationProblem(errors);
    }

    private static IResult InvalidStoreKey()
        => Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["storeKey"] = ["The store key must be lower-case letters, digits and hyphens, starting with a letter (at most 32 characters)."],
        });

    private static TenantDataStoreResponse ToResponse(TenantDataStore row)
        => new(row.TenantId, row.StoreKey, row.ProviderKey, row.Isolation.ToString(), row.Status.ToString(), row.SecretVersion);

    [GeneratedRegex("^[a-z][a-z0-9-]{0,31}$", RegexOptions.CultureInvariant)]
    private static partial Regex StoreKeyRegex();
}
