namespace Modulus.Core.Abstractions.Secrets;

/// <summary>
/// The key/value payload of one secret version. The values are sensitive: this type never prints them, and callers must
/// not log <see cref="Data"/>.
/// </summary>
public sealed class SecretValue
{
    /// <summary>Creates a value.</summary>
    /// <param name="data">The secret's key/value pairs.</param>
    /// <param name="version">The store's version number of the payload (monotonic per secret).</param>
    public SecretValue(IReadOnlyDictionary<string, string> data, int version)
    {
        ArgumentNullException.ThrowIfNull(data);
        Data = data;
        Version = version;
    }

    /// <summary>The secret's key/value pairs.</summary>
    public IReadOnlyDictionary<string, string> Data { get; }

    /// <summary>The store's version number of this payload.</summary>
    public int Version { get; }

    /// <summary>Returns the value stored under <paramref name="key"/>; throws <see cref="KeyNotFoundException"/> if absent.</summary>
    public string Get(string key)
        => Data.TryGetValue(key, out var value)
            ? value
            : throw new KeyNotFoundException($"The secret has no '{key}' field.");

    /// <summary>Always <c>***</c>, so a value cannot leak through string formatting.</summary>
    public override string ToString() => "***";
}
