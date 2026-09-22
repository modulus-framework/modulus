using FluentAssertions;
using Modulus.Localization;
using Xunit;

namespace Modulus.UI.Users.Tests;

/// <summary>
/// H20: <c>UsersUiLocalization.Seed</c> used to special-case
/// <see cref="InMemoryLocalizationStore"/> directly (<c>store is
/// InMemoryLocalizationStore memory</c>) instead of going through
/// <see cref="ILocalizationStore.SetAsync"/>, so a host registering any other
/// store implementation (database-backed, resx-backed, ...) got nothing
/// seeded. This proves <c>SeedAsync</c> now reaches a store that is
/// deliberately NOT <see cref="InMemoryLocalizationStore"/>.
/// </summary>
[Trait("Category", "Unit")]
public sealed class LocalizationSeedTests
{
    /// <summary>A minimal, dictionary-backed store — not InMemoryLocalizationStore.</summary>
    private sealed class FakeLocalizationStore : ILocalizationStore
    {
        private readonly Dictionary<(string, string, string), string> _values = [];

        public Task<string?> GetOrNullAsync(string resourceName, string culture, string key, CancellationToken ct = default)
            => Task.FromResult(_values.TryGetValue((resourceName, culture, key), out var v) ? v : null);

        public Task SetAsync(string resourceName, string culture, string key, string? value, CancellationToken ct = default)
        {
            if (value is null)
                _values.Remove((resourceName, culture, key));
            else
                _values[(resourceName, culture, key)] = value;

            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task SeedAsync_ReachesAnyStore_NotJustInMemoryLocalizationStore()
    {
        var store = new FakeLocalizationStore();

        await UsersUiLocalization.SeedAsync(store);

        (await store.GetOrNullAsync("Modulus.Users", "en", "Users.Title")).Should().Be("Users");
        (await store.GetOrNullAsync("Modulus.Users", "es", "Users.Title")).Should().Be("Usuarios");
    }
}
