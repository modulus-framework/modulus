namespace Modulus.Testing.Authorization;

/// <summary>The outcome of one case of the kit.</summary>
/// <param name="Name">The case, in words.</param>
/// <param name="Problem">Why it failed; null when it passed.</param>
public sealed record AuthorizationConformanceResult(string Name, string? Problem)
{
    public bool Passed => Problem is null;
}

/// <summary>Every case's result, in the order the kit runs them.</summary>
public sealed class AuthorizationConformanceReport(IReadOnlyList<AuthorizationConformanceResult> results)
{
    public IReadOnlyList<AuthorizationConformanceResult> Results { get; } = results;

    public IReadOnlyList<AuthorizationConformanceResult> Failures => [.. Results.Where(r => !r.Passed)];

    /// <summary>Throws when any case failed, naming each one.</summary>
    public void EnsurePassed()
    {
        if (Failures.Count == 0)
            return;

        throw new InvalidOperationException(
            "Authorization conformance failed:" + Environment.NewLine
            + string.Join(Environment.NewLine, Failures.Select(f => $"  [{f.Name}] {f.Problem}")));
    }
}
