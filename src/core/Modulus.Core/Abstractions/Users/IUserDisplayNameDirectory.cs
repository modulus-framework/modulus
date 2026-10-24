namespace Modulus.Core.Abstractions;

/// <summary>
/// The display names of accounts, for views that show who acted (the company audit summary, decision 7). A person who chose no display name
/// is absent from the result, and the view then shows the account id. Never an email address or a login name: those are personal data and
/// this lookup is not a directory search.
/// </summary>
public interface IUserDisplayNameDirectory
{
    /// <summary>The display names of the given accounts. Unknown accounts and accounts without a display name are left out.</summary>
    ValueTask<IReadOnlyDictionary<Guid, string>> GetDisplayNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct = default);
}
