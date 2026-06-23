using BenchmarkDotNet.Attributes;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Domain;

namespace Modulus.Benchmarks;

/// <summary>
/// Benchmarks domain event collection and clearing on AggregateRoot.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 5)]
public class DomainEventBenchmarks
{
    [Params(1, 10, 50, 100)]
    public int EventCount { get; set; }

    [Benchmark]
    public void AddEventsAndClear()
    {
        var root = new TestAggregate();
        for (var i = 0; i < EventCount; i++)
            root.RaiseTestEvent();
        root.ClearDomainEvents();
    }

    private class TestAggregate : AggregateRoot
    {
        public void RaiseTestEvent()
            => AddDomainEvent(new TestDomainEvent());
    }

    private record TestDomainEvent : IDomainEvent
    {
        public Guid EventId { get; } = Guid.NewGuid();
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
    }
}
