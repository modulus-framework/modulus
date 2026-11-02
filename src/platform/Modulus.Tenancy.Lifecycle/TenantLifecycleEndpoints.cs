namespace Modulus.Tenancy.Lifecycle;

using System.IO;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Modulus.Core.Abstractions;

/// <summary>The export as the API shows it.</summary>
public sealed record TenantExportView(
    Guid Id,
    string State,
    DateTimeOffset RequestedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? ExpiresAt,
    bool Downloadable);

/// <summary>Export endpoints for a company's own administrators.</summary>
public static class TenantLifecycleEndpoints
{
    /// <summary>
    /// Maps the export endpoints under <paramref name="prefix"/>, for the current company, behind the <c>tenant-data:export</c> permission:
    /// <c>POST exports</c> (request), <c>GET exports</c> (list), <c>GET exports/{id}</c> (status), <c>GET exports/{id}/download</c>
    /// (the decrypted ZIP, verified before the first byte is sent).
    /// </summary>
    public static IEndpointRouteBuilder MapModulusTenantLifecycle(this IEndpointRouteBuilder app, string prefix = "/tenant-data")
    {
        ArgumentNullException.ThrowIfNull(app);
        var group = app.MapGroup(prefix)
            .WithTags("Tenant data")
            .RequireAuthorization(TenantLifecycleServiceCollectionExtensions.ExportPermission);

        group.MapPost("/exports", async (ITenantLifecycleProvider provider, ICurrentTenant tenant, ClaimsPrincipal user, CancellationToken ct) =>
        {
            if (tenant.TenantId is not { } tenantId)
                return Results.Problem("No company is selected for this request.", statusCode: StatusCodes.Status400BadRequest);
            try
            {
                var operation = await provider.RequestExportAsync(tenantId, UserLabel(user), ct);
                return Results.Accepted($"{prefix}/exports/{operation.Id}", View(operation));
            }
            catch (InvalidOperationException)
            {
                return Results.Problem("Exports are not available: the archive key is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        });

        group.MapGet("/exports", async (ITenantLifecycleProvider provider, ICurrentTenant tenant, CancellationToken ct) =>
        {
            if (tenant.TenantId is not { } tenantId)
                return Results.Problem("No company is selected for this request.", statusCode: StatusCodes.Status400BadRequest);
            var operations = await provider.ListOperationsAsync(tenantId, ct);
            return Results.Ok(operations.Where(o => o.Kind == OperationKind.Export).Select(View));
        });

        group.MapGet("/exports/{id:guid}", async (Guid id, ITenantLifecycleProvider provider, ICurrentTenant tenant, CancellationToken ct) =>
        {
            var operation = await OwnExportAsync(id, provider, tenant, ct);
            return operation is null ? Results.NotFound() : Results.Ok(View(operation));
        });

        group.MapGet("/exports/{id:guid}/download", async (Guid id, HttpContext http, ITenantLifecycleProvider provider, ICurrentTenant tenant, CancellationToken ct) =>
        {
            var operation = await OwnExportAsync(id, provider, tenant, ct);
            if (operation is null)
                return Results.NotFound();

            http.Response.ContentType = "application/zip";
            http.Response.Headers.ContentDisposition = $"attachment; filename=\"export-{id:N}.zip\"";
            try
            {
                // OpenExportAsync verifies the whole archive before it writes, so a refusal here happens before the body starts.
                await provider.OpenExportAsync(id, http.Response.Body, ct);
                return Results.Empty;
            }
            catch (TenantLifecycleRefusedException ex) when (!http.Response.HasStarted)
            {
                http.Response.Headers.Remove("Content-Disposition");
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
            }
            catch (InvalidDataException) when (!http.Response.HasStarted)
            {
                http.Response.Headers.Remove("Content-Disposition");
                return Results.Problem("The export archive failed its integrity check.", statusCode: StatusCodes.Status409Conflict);
            }
        });

        return app;
    }

    private static async Task<LifecycleOperation?> OwnExportAsync(Guid id, ITenantLifecycleProvider provider, ICurrentTenant tenant, CancellationToken ct)
    {
        var operation = await provider.GetOperationAsync(id, ct);
        return operation is not null && operation.Kind == OperationKind.Export && operation.TenantId == tenant.TenantId
            ? operation
            : null;
    }

    private static TenantExportView View(LifecycleOperation operation) => new(
        operation.Id,
        operation.State.ToString(),
        operation.RequestedAt,
        operation.CompletedAt,
        operation.ExportExpiresAt,
        operation.ExportPath is not null && operation.State == OperationState.Completed);

    private static string UserLabel(ClaimsPrincipal user) =>
        user.FindFirst("preferred_username")?.Value ?? user.Identity?.Name ?? user.FindFirst("sub")?.Value ?? "unknown";
}
