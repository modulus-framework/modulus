namespace Modulus.Settings;

using System.Collections.Concurrent;

/// <inheritdoc cref="ISettingDefinitionRegistry" />
public sealed class SettingDefinitionRegistry : ISettingDefinitionRegistry
{
    private readonly ConcurrentDictionary<string, SettingDefinition> _definitions = new();

    public void Add(SettingDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (string.IsNullOrWhiteSpace(definition.Name))
            throw new ArgumentException("Setting name must not be empty.", nameof(definition));

        _definitions[definition.Name] = definition;
    }

    public SettingDefinition? Find(string name)
        => _definitions.TryGetValue(name, out var definition) ? definition : null;

    public IReadOnlyList<SettingDefinition> List()
        => _definitions.Values.OrderBy(d => d.Name, StringComparer.Ordinal).ToList();
}
