using System.Text;

namespace Modulus.Messaging.Internal;

/// <summary>Default endpoint names: <c>OrderSubmittedConsumer</c> becomes <c>order-submitted</c>.</summary>
internal static class EndpointNames
{
    private const string ConsumerSuffix = "Consumer";

    public static string ForConsumer(Type consumerType)
    {
        var name = consumerType.Name;
        if (name.Length > ConsumerSuffix.Length && name.EndsWith(ConsumerSuffix, StringComparison.Ordinal))
        {
            name = name[..^ConsumerSuffix.Length];
        }

        var builder = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c))
            {
                if (i > 0)
                {
                    builder.Append('-');
                }

                builder.Append(char.ToLowerInvariant(c));
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }
}
