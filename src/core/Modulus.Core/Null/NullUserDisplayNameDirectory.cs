using Modulus.Core.Abstractions;

namespace Modulus.Core.Null;

/// <summary>
/// Default <see cref="IUserDisplayNameDirectory"/>: knows no names, so every account shows its id. Replaced when the identity package is
/// registered.
/// </summary>
public sealed class NullUserDisplayNameDirectory : IUserDisplayNameDirectory
{
    /// <summary>The shared instance.</summary>
    public static NullUserDisplayNameDirectory Instance { get; } = new();

    /// <inheritdoc />
    public ValueTask<IReadOnlyDictionary<Guid, string>> GetDisplayNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct = default)
        => ValueTask.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
}
