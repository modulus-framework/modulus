namespace Modulus.Settings;

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
    string? Group = null);

/// <inheritdoc cref="ISettingAdministration" />
public sealed class SettingAdministration(ISettingManager manager, ISettingDefinitionRegistry definitions) : ISettingAdministration
{
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
                definition.DefaultValue,
                resolution.Value,
                resolution.Source,
                resolution.Source == SettingSource.Pinned,
                definition.Group));
        }

        return views;
    }

    public Task SetAsync(string name, string? value, SettingScope scope, CancellationToken ct = default)
    {
        RequireDefinition(name);
        return manager.SetAsync(name, value, scope, ct);
    }

    public Task RemoveAsync(string name, SettingScope scope, CancellationToken ct = default)
    {
        RequireDefinition(name);
        return manager.RemoveAsync(name, scope, ct);
    }

    private void RequireDefinition(string name)
    {
        if (definitions.Find(name) is null)
            throw new NotFoundException($"No setting named '{name}' is registered.");
    }
}
