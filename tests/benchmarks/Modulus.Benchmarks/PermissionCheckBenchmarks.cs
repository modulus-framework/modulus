using BenchmarkDotNet.Attributes;
using Modulus.Authorization;
using Modulus.Authorization.Grants;

namespace Modulus.Benchmarks;

/// <summary>
/// The permission decision (D9, the 10 ms p95 objective): one <see cref="PermissionResolver"/> decision for a principal with
/// <see cref="Roles"/> roles and <see cref="Grants"/> grants. The grant store is in memory, so this measures the resolver and
/// the decision path only. Database I/O (the EF store) is not in these numbers and must be measured separately on the
/// deployment's database.
/// </summary>
[MemoryDiagnoser]
public class PermissionCheckBenchmarks
{
    /// <summary>A grant store that returns a fixed set (no I/O).</summary>
    private sealed class FixedGrantStore(IReadOnlyCollection<PermissionGrant> grants) : IPermissionGrantStore
    {
        public IReadOnlyCollection<PermissionGrant> GetGrants(PrincipalGrantQuery principal) => grants;
    }

    private IReadOnlyCollection<PermissionGrant> _grants = [];
    private PrincipalGrantQuery _query = PrincipalGrantQuery.Anonymous;
    private PermissionRegistry _registry = null!;
    private PermissionResolver _warm = null!;
    private const string Target = "catalog:products:manage";

    /// <summary>How many grants the principal's roles and the company hold.</summary>
    [Params(100, 10_000)]
    public int Grants { get; set; }

    /// <summary>How many roles the principal holds.</summary>
    [Params(1, 5)]
    public int Roles { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _registry = new PermissionRegistry();
        for (var i = 0; i < Grants; i++)
            _registry.Add($"module{i}:thing{i % 7}:manage", "Benchmark permission.");
        _registry.Add(Target, "The permission the benchmark checks.");

        var roles = Enumerable.Range(0, Roles).Select(r => $"role{r}").ToArray();
        var grants = new List<PermissionGrant>(Grants + 1);
        for (var i = 0; i < Grants; i++)
            grants.Add(new PermissionGrant(GrantHolderType.Role, roles[i % roles.Length], $"module{i}:thing{i % 7}:manage", PermissionGrantType.Allow));
        // The target is granted to the last role, so the decision has to walk the whole list.
        grants.Add(new PermissionGrant(GrantHolderType.Role, roles[^1], Target, PermissionGrantType.Allow));
        _grants = grants;
        _query = new PrincipalGrantQuery(Guid.NewGuid(), roles);

        _warm = new PermissionResolver(new FixedGrantStore(_grants), _registry);
        _ = _warm.Resolve(_query);
    }

    /// <summary>The warm decision: the resolver has built its index. The number to compare with the 10 ms p95 objective.</summary>
    [Benchmark(Baseline = true, Description = "Warm decision: resolve the principal's permissions")]
    public bool WarmCheck() => _warm.Resolve(_query).Contains(Target);

    /// <summary>The first decision after startup: a new resolver builds its requires index, so this includes that cost.</summary>
    [Benchmark(Description = "Cold decision: a new resolver, index built on first use")]
    public bool ColdCheck() => new PermissionResolver(new FixedGrantStore(_grants), _registry).Resolve(_query).Contains(Target);
}
