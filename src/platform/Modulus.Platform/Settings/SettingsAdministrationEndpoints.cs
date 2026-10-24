namespace Modulus.Settings;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Authorization.Extensions;
using Modulus.Administration;
using Modulus.Core.Abstractions.Security;
using Modulus.Core.Abstractions;

/// <summary>Body of <c>PUT .../{scope}/{name}</c>. A <see langword="null"/> value removes the stored value.</summary>
/// <param name="Value">The value to store.</param>
public sealed record SettingValueRequest(string? Value);

/// <summary>
/// The setting administration API. Reads serve every setting with its effective value; writes change the value at one scope.
/// Every route needs <see cref="ManagePermission"/>; the installation-wide scope also needs <see cref="GlobalManagePermission"/>,
/// which is Critical, so a company administrator cannot change every company's value.
/// </summary>
public static class SettingsAdministrationEndpoints
{
    /// <summary>Changes a company's or a person's value.</summary>
    public const string ManagePermission = "settings:manage";

    /// <summary>Changes the installation-wide value, which applies to every company.</summary>
    public const string GlobalManagePermission = "settings:global:manage";

    /// <summary>Registers both permissions. Requires <c>AddModulusAuthorization</c> for them to be enforced.</summary>
    public static IServiceCollection AddModulusSettingAdministration(this IServiceCollection services)
        => services.AddPermissions("Modulus.Settings", registry =>
        {
            registry.Add(ManagePermission, "Change the setting values of the current company or person.", null, PermissionSensitivity.Sensitive);
            registry.Add(GlobalManagePermission, "Change the installation-wide setting values, which apply to every company.", null, PermissionSensitivity.Critical);
        });

    /// <summary>
    /// Maps <c>GET {prefix}</c> and <c>PUT|DELETE {prefix}/{global|tenant|user}/{name}</c> (default prefix <c>/api/settings</c>).
    /// </summary>
    public static RouteGroupBuilder MapModulusSettingAdministration(this IEndpointRouteBuilder endpoints, string prefix = "/api/settings")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var group = endpoints.MapGroup(prefix).RequireAuthorization(ManagePermission);

        group.MapGet("", async (ISettingAdministration settings, CancellationToken ct) => Results.Ok(await settings.ListAsync(ct)));

        MapScope(group, "global", SettingScope.Global, GlobalManagePermission);
        MapScope(group, "tenant", SettingScope.Tenant, extraPermission: null);
        MapScope(group, "branch", SettingScope.Branch, extraPermission: null);
        MapScope(group, "user", SettingScope.User, extraPermission: null);

        return group;
    }

    private static void MapScope(RouteGroupBuilder group, string segment, SettingScope scope, string? extraPermission)
    {
        var put = group.MapPut($"/{segment}/{{name}}", (string name, SettingValueRequest body, ISettingAdministration settings, CancellationToken ct) =>
            AdministrationResults.AnswerAsync(() => settings.SetAsync(name, body.Value, scope, ct)));

        var delete = group.MapDelete($"/{segment}/{{name}}", (string name, ISettingAdministration settings, CancellationToken ct) =>
            AdministrationResults.AnswerAsync(() => settings.RemoveAsync(name, scope, ct)));

        if (extraPermission is not null)
        {
            put.RequireAuthorization(extraPermission);
            delete.RequireAuthorization(extraPermission);
        }
    }
}
