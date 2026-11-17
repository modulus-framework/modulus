using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Modulus.Messaging.Sagas;
using Modulus.Messaging.Sagas.EntityFrameworkCore;
using Modulus.Messaging.Testing;
using Xunit;

namespace Modulus.Messaging.Sagas.EntityFrameworkCore.Tests;

[Trait("Category", "Unit")]
public sealed class SagaRepositoryConformanceTests : IAsyncLifetime
{
    private SqliteConnection _keepAlive = null!;

    public async Task InitializeAsync()
    {
        _keepAlive = new SqliteConnection($"DataSource=file:conformance-{Guid.NewGuid():N}?mode=memory&cache=shared");
        await _keepAlive.OpenAsync();
    }

    public async Task DisposeAsync() => await _keepAlive.DisposeAsync();

    [Fact]
    public async Task NFR_16_the_entity_framework_saga_repository_passes_the_conformance_checks()
    {
        await using var db = new SagaTestDb(new DbContextOptionsBuilder<SagaTestDb>().UseSqlite(_keepAlive).Options);
        await db.Database.EnsureCreatedAsync();
        var repository = new EfSagaRepository<TallySaga>(db);

        var report = await SagaRepositoryConformance.RunAsync(
            () => repository,
            id => new TallySaga { CorrelationId = id },
            saga => saga.CurrentState = "Open");

        report.Failed.Should().BeEmpty();
        report.Passed.Should().HaveCount(7);
        report.EnsurePassed();
    }
}
