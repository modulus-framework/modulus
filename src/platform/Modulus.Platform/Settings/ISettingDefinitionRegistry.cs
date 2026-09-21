namespace Modulus.Settings;

/// <summary>
/// Schema registry for <see cref="SettingDefinition"/> entries.
/// Modules contribute definitions at startup; last registration wins so an
/// app can override a module's default (e.g. change a display name).
/// </summary>
public interface ISettingDefinitionRegistry
{
    void Add(SettingDefinition definition);

    SettingDefinition? Find(string name);

    IReadOnlyList<SettingDefinition> List();
}
