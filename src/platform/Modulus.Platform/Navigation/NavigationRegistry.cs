namespace Modulus.Navigation;

/// <summary>Builds the menu from every contributor, once, on first use. A host that never reads the menu never runs them.</summary>
public sealed class NavigationRegistry(IEnumerable<INavigationContributor> contributors)
{
    private readonly Lazy<NavigationDefinition> _definition = new(() =>
    {
        var builder = new NavigationBuilder();
        foreach (var contributor in contributors)
            contributor.Contribute(builder);
        return NavigationDefinition.Build(builder.Items);
    }, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>The checked menu. Throws if a contributor declared something invalid.</summary>
    public NavigationDefinition Definition => _definition.Value;
}

/// <summary>Runs a delegate as a contributor, for items a host declares inline.</summary>
internal sealed class DelegateNavigationContributor(Action<NavigationBuilder> contribute) : INavigationContributor
{
    public void Contribute(NavigationBuilder navigation) => contribute(navigation);
}
