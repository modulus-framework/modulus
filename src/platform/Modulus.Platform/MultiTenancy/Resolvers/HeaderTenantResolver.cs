using Microsoft.AspNetCore.Http;
using Modulus.Core.Abstractions;

namespace Modulus.MultiTenancy.Resolvers;

/// <summary>
/// Resolves the current tenant from the <c>X-Tenant-Id</c> HTTP header (or a
/// custom header name). The header value is interpreted as a tenant GUID
/// first; if parsing fails, it is treated as a tenant slug.
/// </summary>
/// <remarks>
/// <b>Security:</b> This resolver trusts the inbound header value unconditionally.
/// In multi-tenant deployments where tenants share a backend, the header
/// <b>must</b> be set (or overwritten) by a trusted edge component (API
/// gateway, Azure Front Door, Cloudflare Access, etc.) that authenticates the
/// caller and maps them to a tenant — never by the browser or an untrusted
/// client. Typical wiring:
/// <code>
/// // API gateway strips any client-supplied X-Tenant-Id and sets the
/// // authenticated tenant's GUID before forwarding to the backend.
/// services.AddMultiTenancy(t => t.UseHeaderResolver());
/// </code>
/// When running single-tenant (one tenant per host) or behind an edge that
/// always injects the correct tenant, this is the simplest resolver. For
/// deployments where the header might arrive from untrusted callers, prefer
/// <c>UseJwtClaimResolver()</c> (reads the tenant claim from a validated
/// access token) or <c>UseSubdomainResolver()</c>.
/// </remarks>
public sealed class HeaderTenantResolver(
    ITenantStore store,
    string headerName = "X-Tenant-Id")
    : ITenantResolver
{
    public Task<TenantInfo?> ResolveAsync(
        HttpContext ctx, CancellationToken ct)
    {
        var value = ctx.Request.Headers[headerName].FirstOrDefault();
        if (value is null) return Task.FromResult<TenantInfo?>(null);

        return Guid.TryParse(value, out var id)
            ? store.FindByIdAsync(id, ct)
            : store.FindBySlugAsync(value, ct);
    }
}
