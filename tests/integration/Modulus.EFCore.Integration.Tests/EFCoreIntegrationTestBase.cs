namespace Modulus.EFCore.Integration.Tests;

using Microsoft.EntityFrameworkCore;
using Modulus.Core.Abstractions;
using Modulus.Data.PostgreSQL;
using Testcontainers.PostgreSql;
using Xunit;

[Trait("Category", "Integration")]
public abstract class EFCoreIntegrationTestBase : IAsyncLifetime
{
    protected TestCurrentTenant Tenant { get; } = new();

    private readonly PostgreSqlContainer _pg =
        new PostgreSqlBuilder("postgres:16-alpine").Build();

    protected string ConnectionString => _pg.GetConnectionString();

    public Task InitializeAsync() => _pg.StartAsync();
    public Task DisposeAsync()    => _pg.DisposeAsync().AsTask();

    protected TestDbContext BuildContext()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentTenant>(Tenant);
        services.AddSingleton<ICurrentUser>(new TestCurrentUser());
        services.AddSingleton<Modulus.Events.DomainEventDispatcher>();
        services.AddPostgreSQLDatabase<TestDbContext>(ConnectionString);
        var sp  = services.BuildServiceProvider();
        var ctx = sp.GetRequiredService<TestDbContext>();
        ctx.Database.EnsureCreated();
        return ctx;
    }
}