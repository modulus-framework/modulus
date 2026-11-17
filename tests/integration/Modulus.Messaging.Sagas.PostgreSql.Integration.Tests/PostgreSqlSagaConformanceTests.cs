using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Messaging.Sagas;
using Modulus.Messaging.Sagas.PostgreSql;
using Modulus.Messaging.Testing;
using Npgsql;
using Xunit;

namespace Modulus.Messaging.Sagas.PostgreSql.Integration.Tests;

[Trait("Category", "Integration")]
public sealed class PostgreSqlSagaConformanceTests(SagaPostgreSqlBroker database) : IClassFixture<SagaPostgreSqlBroker>
{
    [Fact]
    public async Task NFR_16_the_postgresql_saga_repository_passes_the_conformance_checks()
    {
        const string table = "sagas_conformance";
        await using var dataSource = NpgsqlDataSource.Create(database.ConnectionString);
        await using (var command = dataSource.CreateCommand(PostgreSqlSagaRepository<TallySaga>.SchemaScript(table)))
        {
            await command.ExecuteNonQueryAsync();
        }

        var repository = new PostgreSqlSagaRepository<TallySaga>(dataSource, table);
        var report = await SagaRepositoryConformance.RunAsync(
            () => repository,
            id => new TallySaga { CorrelationId = id },
            saga => saga.CurrentState = "Open");

        report.Failed.Should().BeEmpty();
        report.Passed.Should().HaveCount(7);
        report.EnsurePassed();
    }
}
