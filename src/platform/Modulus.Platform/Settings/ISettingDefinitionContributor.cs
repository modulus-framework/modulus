namespace Modulus.Settings;

/// <summary>
/// Adds setting definitions to the registry when it is first built. A module registers one of these to contribute its settings without
/// touching a central settings page or the shared shell. Contributors run in registration order; a later definition with the same name
/// replaces an earlier one.
/// </summary>
public interface ISettingDefinitionContributor
{
    void Contribute(ISettingDefinitionRegistry registry);
}
