namespace Modulus.BackgroundJobs.Quartz.Integration.Tests;

using Testcontainers.MsSql;
using Testcontainers.PostgreSql;
using Xunit;

[CollectionDefinition(Name)]
public sealed class QuartzPostgresCollection : ICollectionFixture<QuartzPostgresFixture>
{
    public const string Name = "Quartz PostgreSQL";
}

public sealed class QuartzPostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition(Name)]
public sealed class QuartzSqlServerCollection : ICollectionFixture<QuartzSqlServerFixture>
{
    public const string Name = "Quartz SQL Server";
}

public sealed class QuartzSqlServerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}
