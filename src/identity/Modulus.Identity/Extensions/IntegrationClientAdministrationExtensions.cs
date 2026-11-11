using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Authorization.Extensions;
using Modulus.Core.Abstractions;
using Modulus.MultiTenancy;

namespace Modulus.Identity.Extensions;

/// <summary>
/// Administration of integration (machine) clients: which company a partner or agent client acts in, until when, and disabling it.
/// The client's permissions are not set here: grant them to its role <c>integration:{clientId}</c> through the authorization admin API.
/// </summary>
public static class IntegrationClientAdministrationExtensions
{
    /// <summary>Binds, reads and disables integration clients. Critical: a binding decides which company a machine client may act in.</summary>
    public const string ManagePermission = "identity:integration-clients:manage";

    /// <summary>Registers the integration client permission. Requires <c>AddModulusAuthorization</c> for the permission to be enforced.</summary>
    public static IServiceCollection AddModulusIntegrationClientAdministration(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.AddPermissions("Modulus.Identity.IntegrationClients", registry =>
        {
            registry.Add(
                ManagePermission,
                "Bind integration (machine) clients to a company or the host, set their end date, and disable them.",
                null,
                PermissionSensitivity.Critical);
        });
    }

    /// <summary>
    /// Maps <c>{prefix}/{clientId}</c> (read the binding), <c>{prefix}/{clientId}/binding</c> (PUT: bind to a company, or the host with
    /// no company, and an optional end date) and <c>{prefix}/{clientId}/disable</c> (POST). Default prefix <c>/identity/integration-clients</c>.
    /// The routes run in the host context (<see cref="Modulus.MultiTenancy.HostTenantContextEndpointExtensions.RequireHostTenantContext{TBuilder}"/>),
    /// so the platform's grants decide, whatever company the caller selected; a company the caller may not enter is still refused.
    /// </summary>
    public static RouteGroupBuilder MapModulusIntegrationClients(this IEndpointRouteBuilder endpoints, string prefix = "/identity/integration-clients")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var group = endpoints.MapGroup(prefix)
            .RequireHostTenantContext()
            .RequireAuthorization(ManagePermission)
            .AddEndpointFilter(PlatformOperatorOnlyAsync);

        group.MapGet("/{clientId}", async (string clientId, [FromServices] IIntegrationClientDirectory directory, CancellationToken ct) =>
            await directory.FindAsync(clientId, ct) is { } binding
                ? Results.Ok(new { binding.ClientId, binding.TenantId, binding.ValidUntil, binding.MayActForPeople })
                : Results.NotFound());

        group.MapPut("/{clientId}/binding", async (
            string clientId, IntegrationClientBindingRequest body,
            [FromServices] IIntegrationClientDirectory directory, [FromServices] ISecurityAuditLog audit, CancellationToken ct) =>
        {
            if (!await directory.BindAsync(clientId, body.TenantId, body.ValidUntil, ct))
                return Results.NotFound();

            audit.Record(new SecurityAuditEvent
            {
                Category = SecurityAuditCategories.Identity,
                Action = "integration-client.bound",
                TenantId = body.TenantId,
                Target = $"client:{clientId}",
                Details = new Dictionary<string, string?>
                {
                    ["tenant"] = body.TenantId?.ToString() ?? "host",
                    ["validUntil"] = body.ValidUntil?.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                },
            });
            return Results.NoContent();
        });

        group.MapPost("/{clientId}/disable", async (string clientId, [FromServices] IIntegrationClientDirectory directory, [FromServices] ISecurityAuditLog audit, CancellationToken ct) =>
        {
            if (!await directory.DisableAsync(clientId, ct))
                return Results.NotFound();

            audit.Record(new SecurityAuditEvent
            {
                Category = SecurityAuditCategories.Identity,
                Action = "integration-client.disabled",
                Target = $"client:{clientId}",
            });
            return Results.NoContent();
        });

        return group;
    }

    /// <summary>The body of <c>PUT {prefix}/{clientId}/binding</c>. A null <see cref="TenantId"/> binds the client to the host.</summary>
    internal sealed record IntegrationClientBindingRequest(Guid? TenantId, DateTimeOffset? ValidUntil);

    private static async ValueTask<object?> PlatformOperatorOnlyAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var tenant = context.HttpContext.RequestServices.GetRequiredService<ICurrentTenant>();
        if (!tenant.IsHost && tenant.TenantId is not null)
            return Results.Problem(
                title: "Platform operators only",
                detail: "Integration clients are bound by the platform operator, not from inside a company.",
                statusCode: StatusCodes.Status403Forbidden);

        return await next(context);
    }
}
