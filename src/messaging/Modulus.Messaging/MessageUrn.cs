using System.Reflection;

namespace Modulus.Messaging;

/// <summary>The wire identity of a message type (RULE-10).</summary>
internal static class MessageUrn
{
    /// <summary>The prefix of a URN derived from a type that has no <see cref="MessageUrnAttribute"/>.</summary>
    internal const string DerivedPrefix = "urn:clr:";

    /// <summary>The prefix every message URN starts with.</summary>
    internal const string UrnPrefix = "urn:";

    /// <summary>The URN the type is sent under: its <see cref="MessageUrnAttribute"/> when it has one, else a derived URN.</summary>
    public static string Of(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (type.GetCustomAttribute<MessageUrnAttribute>(inherit: false) is { } attribute)
        {
            return attribute.Urn;
        }

        return DerivedPrefix + (type.FullName ?? type.Name);
    }

    /// <summary>
    /// Refuses two different types under one URN: a receiver could not tell them apart. Checked at bus build for the
    /// registered message types.
    /// </summary>
    public static void EnsureUnique(IEnumerable<Type> types)
    {
        var owners = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (var type in types.Distinct())
        {
            var urn = Of(type);
            if (owners.TryGetValue(urn, out var other) && other != type)
            {
                throw new InvalidOperationException(
                    $"Message types '{other.FullName}' and '{type.FullName}' both use the URN '{urn}'. Give one of them a different [MessageUrn].");
            }

            owners[urn] = type;
        }
    }
}
