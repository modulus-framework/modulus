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
    bool IsVisibleToClients = true);
