namespace Modulus.Settings;

/// <summary>
/// Declares a single settable value (ABP-style setting definition).
/// Definitions are the schema; actual values live in <see cref="ISettingStore"/>
/// per scope (<see cref="SettingScope"/>).
/// </summary>
public sealed record SettingDefinition(
    string Name,
    string? DefaultValue = null,
    string? DisplayName = null,
    string? Description = null,
    bool IsVisibleToClients = true)
{
    /// <summary>What a value must parse as. Writes that do not parse are refused. Default <see cref="SettingValueType.String"/>.</summary>
    public SettingValueType ValueType { get; init; } = SettingValueType.String;

    /// <summary>
    /// When not empty, the only values accepted (case-insensitive), for example <c>["cash", "card"]</c>. Empty means any value of
    /// <see cref="ValueType"/>.
    /// </summary>
    public IReadOnlyList<string> AllowedValues { get; init; } = [];

    /// <summary>The scopes a value may be stored at. Default every scope. A value at a scope not listed here is refused.</summary>
    public SettingScopes AllowedScopes { get; init; } = SettingScopes.All;

    /// <summary>
    /// The group the settings screen files this setting under (decision 28), for example <c>Sales</c> or <c>Security</c>. Null means ungrouped.
    /// </summary>
    public string? Group { get; init; }
}
