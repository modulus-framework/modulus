using BenchmarkDotNet.Attributes;
using Modulus.Authorization.Scopes;

namespace Modulus.Benchmarks;

/// <summary>
/// The compiled scope predicates (A5-5). <see cref="CompileOnFirstUse"/> is what a map costs the first time it is used; the
/// <see cref="EvaluateAll"/> pair is the per-record cost of a warm map, which every list, count and record check pays.
/// </summary>
[MemoryDiagnoser]
public class ScopePredicateBenchmarks
{
    public sealed class Shipment
    {
        public Guid? OwnerId { get; init; }

        public Guid? OrgUnitId { get; init; }

        public Guid? WarehouseId { get; init; }

        public Guid? CustomerId { get; init; }

        public Guid? RegionId { get; init; }
    }

    private Shipment[] _records = [];
    private ScopeMap<Shipment> _map = null!;

    /// <summary>How many records the warm evaluation walks.</summary>
    [Params(1_000, 100_000)]
    public int Records { get; set; }

    /// <summary>How many assignment types the map declares (0 to 3), each a compiled selector.</summary>
    [Params(0, 1, 3)]
    public int AssignmentTypes { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        // Seeded, so every run sees the same data: a tenth of the owners and org units are unset.
        var random = new Random(42);
        _records = Enumerable.Range(0, Records).Select(_ => new Shipment
        {
            OwnerId = Maybe(random),
            OrgUnitId = Maybe(random),
            WarehouseId = Maybe(random),
            CustomerId = Maybe(random),
            RegionId = Maybe(random),
        }).ToArray();

        // Compile once here, so EvaluateAll measures only the warm path.
        _map = BuildMap();
        _ = _map.FactsOf(_records[0]);
    }

    [Benchmark(Description = "Build and compile a map, then evaluate one record")]
    public ScopeFacts CompileOnFirstUse() => BuildMap().FactsOf(_records[0]);

    [Benchmark(Baseline = true, Description = "Evaluate a warm map over every record")]
    public int EvaluateAll()
    {
        var owned = 0;
        foreach (var record in _records)
        {
            if (_map.FactsOf(record).OwnerId is not null)
                owned++;
        }

        return owned;
    }

    private ScopeMap<Shipment> BuildMap()
    {
        var builder = new ScopeMapBuilder<Shipment>()
            .Owner(s => s.OwnerId)
            .OrgUnit(s => s.OrgUnitId);
        if (AssignmentTypes >= 1)
            builder.AssignedKey("warehouse", s => s.WarehouseId);
        if (AssignmentTypes >= 2)
            builder.AssignedKey("customer", s => s.CustomerId);
        if (AssignmentTypes >= 3)
            builder.AssignedKey("region", s => s.RegionId);
        return builder.Build();
    }

    private static Guid? Maybe(Random random)
    {
        if (random.Next(10) == 0)
            return null;
        var bytes = new byte[16];
        random.NextBytes(bytes);
        return new Guid(bytes);
    }
}
