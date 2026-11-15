using System.Text.Json;

namespace Modulus.Messaging.Internal;

/// <summary>
/// The JSON options a sealed body is written and read with. They match the transports' codecs (web defaults), so the
/// plaintext the bus seals is the same text a transport would have written.
/// </summary>
internal static class MessageJson
{
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
