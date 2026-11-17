using FluentAssertions;
using Modulus.Messaging.Sagas;
using Modulus.Messaging.Testing;
using Xunit;

namespace Modulus.Messaging.Sagas.Tests;

[Trait("Category", "Unit")]
public sealed class SagaRepositoryConformanceTests
{
    [Fact]
    public async Task NFR_16_the_in_memory_saga_repository_passes_the_conformance_checks()
    {
        var repository = new InMemorySagaRepository<TallySaga>();

        var report = await SagaRepositoryConformance.RunAsync(
            () => repository,
            id => new TallySaga { CorrelationId = id },
            saga => saga.CurrentState = "Open");

        report.Failed.Should().BeEmpty();
        report.Passed.Should().HaveCount(7);
        report.EnsurePassed();
    }
}
