namespace Modulus.Core.Abstractions.Secrets;

/// <summary>
/// A credential-bearing string, such as a database connection string. It prints as <c>***</c>; the only way to read it is
/// <see cref="Reveal"/>, so an accidental log or serialisation cannot expose it.
/// </summary>
public sealed class SensitiveString
{
    private readonly string _value;

    /// <summary>Wraps <paramref name="value"/>.</summary>
    public SensitiveString(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        _value = value;
    }

    /// <summary>Returns the clear text. Call it only at the point of use (for example, when opening a connection).</summary>
    public string Reveal() => _value;

    /// <summary>Always <c>***</c>.</summary>
    public override string ToString() => "***";
}
