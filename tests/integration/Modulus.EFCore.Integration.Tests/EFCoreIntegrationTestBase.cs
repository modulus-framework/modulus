namespace Modulus.EFCore.Integration.Tests;

using Microsoft.EntityFrameworkCore;
using Modulus.Core.Abstractions;
using Modulus.Data.PostgreSQL;
using Testcontainers.PostgreSql;
using Xunit;

public sealed class PostgreSqlFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container =
        new PostgreSqlBuilder("postgres:16-alpine").Build();

    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();
    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[Trait("Category", "Integration")]
public abstract class EFCoreIntegrationTestBase
{
    protected TestCurrentTenant Tenant { get; } = new();

    protected string ConnectionString { get; }

    protected EFCoreIntegrationTestBase(PostgreSqlFixture fixture)
    {
        ConnectionString = fixture.ConnectionString;
    }

    protected TestDbContext BuildContext()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentTenant>(Tenant);
        services.AddSingleton<ICurrentUser>(new TestCurrentUser());
        services.AddSingleton<Modulus.Events.DomainEventDispatcher>();
        services.AddPostgreSQLDatabase<TestDbContext>(ConnectionString);
        var sp = services.BuildServiceProvider();
        var ctx = sp.GetRequiredService<TestDbContext>();
        ctx.Database.EnsureCreated();
        return ctx;
    }
}
