namespace Modulus.Settings;

using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Exceptions;

/// <summary>
/// The administrator's view of settings: every registered definition with its effective value in the current context, and
/// validated writes. Only registered names can be written here, so an administrator cannot create an arbitrary setting. Callers
/// authorize the request; <see cref="SettingsAdministrationEndpoints"/> does it for the HTTP API.
/// </summary>
public interface ISettingAdministration
{
    /// <summary>Every registered definition, with the value that applies to the current company and person.</summary>
    Task<IReadOnlyList<SettingView>> ListAsync(CancellationToken ct = default);

    /// <summary>Stores a value at <paramref name="scope"/>. A name with no definition is <c>NotFound</c>.</summary>
    Task SetAsync(string name, string? value, SettingScope scope, CancellationToken ct = default);

    /// <summary>Removes the value at <paramref name="scope"/>. A name with no definition is <c>NotFound</c>.</summary>
    Task RemoveAsync(string name, SettingScope scope, CancellationToken ct = default);
}

/// <summary>One setting as an administrator sees it.</summary>
/// <param name="Name">The setting.</param>
/// <param name="DisplayName">The display name, when the definition has one.</param>
/// <param name="Description">The description, when the definition has one.</param>
/// <param name="ValueType">What the value must parse as.</param>
/// <param name="AllowedValues">The only accepted values, or empty for any.</param>
/// <param name="AllowedScopes">The scopes a value may be stored at.</param>
/// <param name="DefaultValue">The definition's default.</param>
/// <param name="EffectiveValue">The value that applies in the current context.</param>
/// <param name="Source">Where <paramref name="EffectiveValue"/> came from.</param>
/// <param name="IsPinned">Whether the host pins the value (no scope can change it).</param>
/// <param name="Group">The group the settings screen files the setting under, or null.</param>
/// <param name="IsSensitive">True for a secret; <paramref name="EffectiveValue"/> is then <see cref="SettingMasking.Mask"/> when a value is set.</param>
public sealed record SettingView(
    string Name,
    string? DisplayName,
    string? Description,
    SettingValueType ValueType,
    IReadOnlyList<string> AllowedValues,
    SettingScopes AllowedScopes,
    string? DefaultValue,
    string? EffectiveValue,
    SettingSource Source,
    bool IsPinned,
    string? Group = null,
    bool IsSensitive = false);

/// <summary>How administrator reads show a secret.</summary>
public static class SettingMasking
{
    /// <summary>Stands in for a secret's value in every administrator read. It is never a real value.</summary>
    public const string Mask = "********";
}

/// <inheritdoc cref="ISettingAdministration" />
/// <param name="manager">The setting manager that validates and stores the values.</param>
/// <param name="definitions">The registered definitions.</param>
/// <param name="audit">Receives one event per successful change. Optional: without a security audit log, changes are not recorded here.</param>
/// <param name="currentUser">The person making the change, recorded as the actor. Optional.</param>
/// <param name="currentTenant">The company whose chain receives the event. Optional: without it the host chain is used.</param>
public sealed class SettingAdministration(
    ISettingManager manager,
    ISettingDefinitionRegistry definitions,
    ISecurityAuditLog? audit = null,
    ICurrentUser? currentUser = null,
    ICurrentTenant? currentTenant = null) : ISettingAdministration
{
    /// <summary>The audit category for setting changes.</summary>
    public const string AuditCategory = "settings";

    /// <summary>The audit action for a changed value.</summary>
    public const string ChangedAction = "setting.changed";

    /// <summary>The audit action for a removed value.</summary>
    public const string RemovedAction = "setting.removed";

    public async Task<IReadOnlyList<SettingView>> ListAsync(CancellationToken ct = default)
    {
        var views = new List<SettingView>();
        foreach (var definition in definitions.List())
        {
            var resolution = await manager.ResolveAsync(definition.Name, ct).ConfigureAwait(false);
            views.Add(new SettingView(
                definition.Name,
                definition.DisplayName,
                definition.Description,
                definition.ValueType,
                definition.AllowedValues,
                definition.AllowedScopes,
                definition.IsSensitive ? null : definition.DefaultValue,
                MaskIfSensitive(definition, resolution),
                resolution.Source,
                resolution.Source == SettingSource.Pinned,
                definition.Group,
                definition.IsSensitive));
        }

        return views;
    }

    public async Task SetAsync(string name, string? value, SettingScope scope, CancellationToken ct = default)
    {
        var definition = RequireDefinition(name);
        var before = await manager.ResolveAsync(name, ct).ConfigureAwait(false);
        await manager.SetAsync(name, value, scope, ct).ConfigureAwait(false);
        Record(ChangedAction, definition, scope, before, value);
    }

    public async Task RemoveAsync(string name, SettingScope scope, CancellationToken ct = default)
    {
        var definition = RequireDefinition(name);
        var before = await manager.ResolveAsync(name, ct).ConfigureAwait(false);
        await manager.RemoveAsync(name, scope, ct).ConfigureAwait(false);
        Record(RemovedAction, definition, scope, before, null);
    }

    // One event per successful change. The values go into the chain only for settings that are not sensitive; a secret is recorded as a
    // change with no values, so the audit trail can say that it changed without holding it. "Before" is the value that applied before the
    // change (the effective value), not the value at this scope alone.
    private void Record(string action, SettingDefinition definition, SettingScope scope, SettingResolution before, string? after)
    {
        if (audit is null)
            return;

        var details = new Dictionary<string, string?>
        {
            ["scope"] = scope.ToString(),
            ["sensitive"] = definition.IsSensitive ? "true" : "false",
        };
        if (!definition.IsSensitive)
        {
            details["before"] = before.Value;
            details["after"] = after;
        }

        audit.Record(new SecurityAuditEvent
        {
            Category = AuditCategory,
            Action = action,
            TenantId = currentTenant?.TenantId,
            Actor = currentUser?.UserId?.ToString("D"),
            Target = definition.Name,
            Details = details,
        });
    }

    // A secret that no scope or pin sets shows nothing, so a default is never mistaken for a stored value. A stored or pinned secret shows the mask.
    private static string? MaskIfSensitive(SettingDefinition definition, SettingResolution resolution)
    {
        if (!definition.IsSensitive)
            return resolution.Value;

        var stored = resolution.Source is not (SettingSource.Default or SettingSource.None);
        return stored && resolution.Value is not null ? SettingMasking.Mask : null;
    }

    private SettingDefinition RequireDefinition(string name)
        => definitions.Find(name) ?? throw new NotFoundException($"No setting named '{name}' is registered.");
}
