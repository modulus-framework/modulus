using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace Modulus.Messaging.Internal;

/// <summary>
/// A short, redacted description of a message for diagnostics: its type and its simple properties. Properties marked
/// <see cref="SensitiveAttribute"/> show as <c>***</c>, and complex values show only their type name.
/// </summary>
internal static class MessageDescription
{
    private const int MaxValueLength = 120;
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> Properties = new();

    internal static string Describe(object message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var type = message.GetType();
        var builder = new StringBuilder(type.Name).Append(" { ");
        var first = true;
        foreach (var property in Properties.GetOrAdd(type, static t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .Where(p => p.GetIndexParameters().Length == 0 && p.CanRead)
                     .ToArray()))
        {
            if (!first)
            {
                builder.Append(", ");
            }

            first = false;
            builder.Append(property.Name).Append('=').Append(ValueOf(property, message));
        }

        return builder.Append(" }").ToString();
    }

    private static string ValueOf(PropertyInfo property, object message)
    {
        if (property.IsDefined(typeof(SensitiveAttribute), inherit: true))
        {
            return "***";
        }

        var value = property.GetValue(message);
        if (value is null)
        {
            return "null";
        }

        if (value is string or Guid or bool or char or DateTime or DateTimeOffset or TimeSpan or decimal or Enum || value.GetType().IsPrimitive)
        {
            var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            return text.Length <= MaxValueLength ? text : text[..MaxValueLength] + "...";
        }

        return value.GetType().Name;
    }
}
