namespace Modulus.EntityFrameworkCore.Tests;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.Core.Null;
using Modulus.EntityFrameworkCore.Extensions;
using Modulus.Events;
using FluentAssertions;
using Xunit;

[Trait("Category", "Unit")]
public sealed class SyncSaveChangesTests : IAsyncDisposable
{
    private readonly SqliteConnection _conn;
    private readonly ServiceProvider _sp;
    private readonly TestDbContext _db;

    public SyncSaveChangesTests()
    {
        _conn = new SqliteConnection("DataSource=sync-save;Mode=Memory;Cache=Shared");
        _conn.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<ICurrentTenant, HostTenant>();
        services.AddScoped<ICurrentUser, NullCurrentUser>();
        services.AddScoped<DomainEventDispatcher>();
        services.AddModuleDatabase<TestDbContext>(o => o.UseSqlite(_conn));
        _sp = services.BuildServiceProvider();
        _db = _sp.GetRequiredService<TestDbContext>();
    }

    [Fact]
    public void SyncSaveChanges_Throws_DirectsToAsync()
    {
        var act = () => _db.SaveChanges();
        act.Should().Throw<NotSupportedException>()
            .WithMessage("*SaveChangesAsync*");
    }

    [Fact]
    public void SyncSaveChangesWithAcceptAll_Throws_DirectsToAsync()
    {
        var act = () => _db.SaveChanges(acceptAllChangesOnSuccess: true);
        act.Should().Throw<NotSupportedException>()
            .WithMessage("*SaveChangesAsync*");
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _db.DisposeAsync();
        await _sp.DisposeAsync();
        _conn.Dispose();
    }

    private sealed class HostTenant : ICurrentTenant
    {
        public Guid? TenantId => null;
        public string? TenantSlug => null;
        public bool IsAvailable => true;
        public bool IsHost => true;

        public IDisposable Change(TenantInfo? tenant) => new NoopDisposable();
        public IDisposable BeginScope() => new NoopDisposable();

        private sealed class NoopDisposable : IDisposable
        {
            public void Dispose() { }
        }
    }

    private sealed class TestDbContext : ModuleDbContext
    {
        public TestDbContext(DbContextOptions options, ICurrentTenant tenant, ICurrentUser user,
            DomainEventDispatcher dispatcher, IServiceProvider sp)
            : base(options, tenant, user, dispatcher, sp)
        {
        }

        protected override string TablePrefix => "test_";
    }
}
