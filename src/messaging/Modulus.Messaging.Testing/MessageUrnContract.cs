namespace Modulus.Messaging.Testing;

/// <summary>
/// The message contract of a service, recorded in a file under source control (RULE-11). Each line is a URN and the message
/// type it names. A URN that was recorded and is gone now is a breaking change: the message can no longer be consumed under
/// its identity, so the check fails. A new URN is recorded automatically. Removing one on purpose is accepted only when
/// <c>acceptRemovals</c> is set (the CI variable <c>MODULUS_ACCEPT_MESSAGE_CONTRACT=1</c>), so the decision shows in the diff.
/// </summary>
public static class MessageUrnContract
{
    /// <summary>Compares the message types with the recorded contract, recording the new ones.</summary>
    /// <param name="contractPath">The contract file. It is created when it does not exist.</param>
    /// <param name="messageTypes">The message types of the service.</param>
    /// <param name="acceptRemovals">Accepts recorded URNs that are gone, and drops them from the file.</param>
    public static MessageUrnContractReport Verify(string contractPath, IEnumerable<Type> messageTypes, bool acceptRemovals = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contractPath);
        ArgumentNullException.ThrowIfNull(messageTypes);

        var current = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var type in messageTypes.Distinct())
        {
            var urn = MessageUrns.Of(type);
            if (current.TryGetValue(urn, out var other) && other != type.FullName)
            {
                throw new InvalidOperationException($"Message types '{other}' and '{type.FullName}' both use the URN '{urn}'.");
            }

            current[urn] = type.FullName ?? type.Name;
        }

        var recorded = File.Exists(contractPath)
            ? File.ReadAllLines(contractPath)
                .Where(line => line.Length > 0 && !line.StartsWith('#'))
                .Select(line => line.Split(' ', 2))
                .ToDictionary(parts => parts[0], parts => parts.Length > 1 ? parts[1] : string.Empty, StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal);

        var removed = recorded.Keys.Where(urn => !current.ContainsKey(urn)).OrderBy(urn => urn, StringComparer.Ordinal).ToArray();
        var added = current.Keys.Where(urn => !recorded.ContainsKey(urn)).OrderBy(urn => urn, StringComparer.Ordinal).ToArray();

        var keep = acceptRemovals ? current : recorded.Where(pair => current.ContainsKey(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        foreach (var pair in current)
        {
            keep.TryAdd(pair.Key, pair.Value);
        }

        var lines = keep.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key} {pair.Value}");
        var newContent = string.Join('\n', lines) + "\n";
        var changed = !File.Exists(contractPath) || File.ReadAllText(contractPath) != newContent;
        if (changed && (removed.Length == 0 || acceptRemovals))
        {
            File.WriteAllText(contractPath, newContent);
        }

        return new MessageUrnContractReport(removed, added);
    }
}

/// <summary>What a contract check found: URNs that are gone (a breaking change) and URNs that are new (recorded).</summary>
public sealed record MessageUrnContractReport(IReadOnlyList<string> Removed, IReadOnlyList<string> Added)
{
    /// <summary>Throws listing the recorded URNs that are gone, when there are any.</summary>
    public void EnsureNoBreakingChange()
    {
        if (Removed.Count > 0)
        {
            throw new InvalidOperationException(
                "Recorded message URNs are gone (a breaking change, RULE-11): " + string.Join(", ", Removed)
                + ". Keep the URN, or add the new version under a new URN and accept the removal on purpose with MODULUS_ACCEPT_MESSAGE_CONTRACT=1.");
        }
    }
}
