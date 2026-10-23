namespace Modulus.Settings;

/// <summary>The effective value of a setting in the current context, and the scope it came from.</summary>
/// <param name="Name">The setting.</param>
/// <param name="Value">The effective value, or <see langword="null"/> when there is none.</param>
/// <param name="Source">Where <paramref name="Value"/> came from.</param>
public sealed record SettingResolution(string Name, string? Value, SettingSource Source);
