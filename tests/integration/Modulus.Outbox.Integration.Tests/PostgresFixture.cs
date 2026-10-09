namespace Modulus.Outbox.Integration.Tests;

using Microsoft.EntityFrameworkCore;
using Modulus.Inbox.Abstractions;
using Modulus.Outbox.Abstractions;
using Testcontainers.PostgreSql;
using Xunit;

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "Postgres outbox/inbox";
}

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var db = CreateContext();
        await db.Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public MessagingDbContext CreateContext()
        => new(new DbContextOptionsBuilder<MessagingDbContext>().UseNpgsql(ConnectionString).Options);
}

public sealed class MessagingDbContext(DbContextOptions<MessagingDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<OutboxMessage>(b =>
        {
            b.ToTable("outbox_messages");
            b.HasKey(x => x.Id);
            b.Property(x => x.MessageType).HasMaxLength(500).IsRequired();
            b.Property(x => x.Payload).IsRequired();
            b.Property(x => x.ModuleName).HasMaxLength(100);
        });
        new Modulus.Inbox.Configurations.InboxMessageConfiguration().Configure(mb.Entity<InboxMessage>());
    }
}
