using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;

namespace Modulus.Identity;

/// <summary>
/// The server-side facts about an integration (machine) client: the company it is bound to and until when it may get tokens.
/// What the client may <i>do</i> is not here: its token carries the role <see cref="IntegrationClients.RoleFor"/>, and permissions,
/// scopes and expiry are granted to that role through the ordinary grant store (admin API <c>grants</c>, <c>scoped-grants</c>), so a
/// grant removed stops working on the next request, not when the token expires.
/// </summary>
/// <param name="ClientId">The OpenIddict client id.</param>
/// <param name="TenantId">The one company the client acts in (its tokens carry it as <c>tid</c>); null for a host-level client.</param>
/// <param name="ValidUntil">After this instant the client is refused new tokens; null for no end.</param>
/// <param name="MayActForPeople">Whether the agent may act for signed-in people through token exchange (spec 12A); off by default.</param>
public sealed record IntegrationClientBinding(string ClientId, Guid? TenantId, DateTimeOffset? ValidUntil, bool MayActForPeople = false);

/// <summary>Reads and writes the binding of an integration client, kept in its OpenIddict application record.</summary>
public interface IIntegrationClientDirectory
{
    /// <summary>The binding of <paramref name="clientId"/>; null when the client does not exist.</summary>
    Task<IntegrationClientBinding?> FindAsync(string clientId, CancellationToken ct = default);

    /// <summary>Binds the client to a company (or the host, with null) and an optional end date. False when the client does not exist.</summary>
    Task<bool> BindAsync(string clientId, Guid? tenantId, DateTimeOffset? validUntil, CancellationToken ct = default);

    /// <summary>Ends the client now: it gets no new tokens and every token it holds is revoked. False when the client does not exist.</summary>
    Task<bool> DisableAsync(string clientId, CancellationToken ct = default);

    /// <summary>Whether the agent may act for people (token exchange, spec 12A). Off by default; an administrator turns it on per agent.</summary>
    Task<bool> SetMayActForPeopleAsync(string clientId, bool mayActForPeople, CancellationToken ct = default) => Task.FromResult(false);
}

/// <summary>Names used for integration clients.</summary>
public static class IntegrationClients
{
    /// <summary>
    /// The prefix reserved for integration client roles. People cannot be given a role with it (the user manager refuses), so
    /// holding one proves nothing beyond the machine marker the token server adds.
    /// </summary>
    internal const string RolePrefix = "integration:";

    /// <summary>The role claim an integration client's token carries; grant permissions to this role.</summary>
    public static string RoleFor(string clientId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        return $"{RolePrefix}{clientId}";
    }
}

internal sealed class OpenIddictIntegrationClientDirectory(IServiceProvider services) : IIntegrationClientDirectory
{
    private const string TenantProperty = "modulus:tenant";
    private const string ValidUntilProperty = "modulus:valid-until";
    private const string MayActForPeopleProperty = "modulus:may-act-for-people";

    public async Task<IntegrationClientBinding?> FindAsync(string clientId, CancellationToken ct = default)
    {
        if (services.GetService<IOpenIddictApplicationManager>() is not { } apps
            || await apps.FindByClientIdAsync(clientId, ct) is not { } app)
            return null;

        var properties = await apps.GetPropertiesAsync(app, ct);
        Guid? tenant = properties.TryGetValue(TenantProperty, out var t) && Guid.TryParse(t.GetString(), out var id) ? id : null;
        DateTimeOffset? until = properties.TryGetValue(ValidUntilProperty, out var u) && DateTimeOffset.TryParse(
            u.GetString(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var when) ? when : null;
        var mayAct = properties.TryGetValue(MayActForPeopleProperty, out var m) && m.ValueKind == System.Text.Json.JsonValueKind.True;
        return new IntegrationClientBinding(clientId, tenant, until, mayAct);
    }

    public async Task<bool> BindAsync(string clientId, Guid? tenantId, DateTimeOffset? validUntil, CancellationToken ct = default)
    {
        if (services.GetService<IOpenIddictApplicationManager>() is not { } apps
            || await apps.FindByClientIdAsync(clientId, ct) is not { } app)
            return false;

        var descriptor = new OpenIddictApplicationDescriptor();
        await apps.PopulateAsync(descriptor, app, ct);
        Set(descriptor, TenantProperty, tenantId?.ToString());
        Set(descriptor, ValidUntilProperty, validUntil?.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        await apps.UpdateAsync(app, descriptor, ct);
        return true;
    }

    public async Task<bool> SetMayActForPeopleAsync(string clientId, bool mayActForPeople, CancellationToken ct = default)
    {
        if (services.GetService<IOpenIddictApplicationManager>() is not { } apps
            || await apps.FindByClientIdAsync(clientId, ct) is not { } app)
            return false;

        var descriptor = new OpenIddictApplicationDescriptor();
        await apps.PopulateAsync(descriptor, app, ct);
        descriptor.Properties[MayActForPeopleProperty] = JsonSerializer.SerializeToElement(mayActForPeople);
        await apps.UpdateAsync(app, descriptor, ct);
        return true;
    }

    public async Task<bool> DisableAsync(string clientId, CancellationToken ct = default)
    {
        if (!await BindAsync(clientId, (await FindAsync(clientId, ct))?.TenantId, DateTimeOffset.UtcNow, ct))
            return false;

        if (services.GetService<IOpenIddictTokenManager>() is { } tokens)
        {
            // A client token's subject is the client id.
            await foreach (var token in tokens.FindBySubjectAsync(clientId, ct))
                await tokens.TryRevokeAsync(token, ct);
        }

        return true;
    }

    private static void Set(OpenIddictApplicationDescriptor descriptor, string name, string? value)
    {
        if (value is null)
            descriptor.Properties.Remove(name);
        else
            descriptor.Properties[name] = JsonSerializer.SerializeToElement(value);
    }
}
