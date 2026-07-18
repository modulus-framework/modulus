namespace Modulus.EntityFrameworkCore.Tests;

using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.DataProtection;
using Modulus.Core.Null;
using Modulus.EntityFrameworkCore.Extensions;
using Modulus.Events;
using FluentAssertions;
using Xunit;

// Exercises the value converter + model hook that ModuleDbContext applies when an
// IPersonalDataProtector is registered. A reversible fake protector stands in for Data
// Protection so the test proves the wiring (ciphertext at rest, plaintext in memory,
// only [ProtectedPersonalData] columns affected), not the crypto library.
[Trait("Category", "Unit")]
public sealed class PersonalDataEncryptionTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _root;

    public PersonalDataEncryptionTests()
    {
        _connection = new SqliteConnection("DataSource=pii-mem;Mode=Memory;Cache=Shared");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<ICurrentTenant, NullCurrentTenant>();
        services.AddScoped<ICurrentUser, NullCurrentUser>();
        services.AddScoped<DomainEventDispatcher>();
        services.AddSingleton<IPersonalDataProtector, FakeProtector>();
        services.AddModuleDatabase<PeopleDbContext>(o => o.UseSqlite(_connection));
        _root = services.BuildServiceProvider();

        using var scope = _root.CreateScope();
        scope.ServiceProvider.GetRequiredService<PeopleDbContext>()
            .Database.EnsureCreated();
    }

    [Fact]
    public async Task EncryptsMarkedColumnAtRest_ButMaterialisesPlaintext()
    {
        var id = Guid.NewGuid();
        await using (var scope = _root.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PeopleDbContext>();
            db.People.Add(new Person { Id = id, Name = "Ada Lovelace", Email = "ada@example.com" });
            await db.SaveChangesAsync();
        }

        // Raw column holds ciphertext, not the plaintext email.
        var rawEmail = await ReadRawColumnAsync("Email");
        rawEmail.Should().NotBe("ada@example.com");
        rawEmail.Should().StartWith("ENC:");

        // The unmarked Name column is stored as-is.
        var rawName = await ReadRawColumnAsync("Name");
        rawName.Should().Be("Ada Lovelace");

        // Reading back through EF transparently decrypts.
        await using var readScope = _root.CreateAsyncScope();
        var loaded = await readScope.ServiceProvider.GetRequiredService<PeopleDbContext>()
            .People.SingleAsync(p => p.Id == id);
        loaded.Email.Should().Be("ada@example.com");
    }

    [Fact]
    public async Task DeterministicHash_EnablesEqualitySearchOnEncryptedField()
    {
        var protector = _root.GetRequiredService<IPersonalDataProtector>();
        var id = Guid.NewGuid();
        const string email = "grace@example.com";

        await using (var scope = _root.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PeopleDbContext>();
            db.People.Add(new Person
            {
                Id = id,
                Name = "Grace Hopper",
                Email = email,
                EmailHash = protector.Hash(email),
            });
            await db.SaveChangesAsync();
        }

        // The hash is deterministic, so the same input recomputes the same lookup key.
        var lookup = protector.Hash(email);
        await using var readScope = _root.CreateAsyncScope();
        var found = await readScope.ServiceProvider.GetRequiredService<PeopleDbContext>()
            .People.SingleAsync(p => p.EmailHash == lookup);

        found.Id.Should().Be(id);
        found.Email.Should().Be(email);
    }

    private async Task<string?> ReadRawColumnAsync(string column)
    {
        // Resolve the physical table name from the model (ModuleDbContext prefixes it)
        // rather than hard-coding it, then read the column with a raw ADO query so the
        // value seen is exactly what is stored on disk — no EF converter in the path.
        // The test inserts a single Person into a fresh in-memory database.
        await using var scope = _root.CreateAsyncScope();
        var table = scope.ServiceProvider.GetRequiredService<PeopleDbContext>()
            .Model.FindEntityType(typeof(Person))!.GetTableName();

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"SELECT \"{column}\" FROM \"{table}\" LIMIT 1";
        var value = await cmd.ExecuteScalarAsync();
        return value as string;
    }

    public async ValueTask DisposeAsync()
    {
        await _root.DisposeAsync();
        await _connection.DisposeAsync();
    }

    // Reversible stand-in for Data Protection: prefixes so ciphertext is visibly
    // distinct from plaintext, with a deterministic keyed hash for search.
    private sealed class FakeProtector : IPersonalDataProtector
    {
        private static readonly byte[] Key = Encoding.UTF8.GetBytes("test-hmac-key");
        public string Protect(string plaintext) => "ENC:" + plaintext;
        public string Unprotect(string ciphertext) => ciphertext["ENC:".Length..];
        public string Hash(string value) =>
            Convert.ToBase64String(HMACSHA256.HashData(Key, Encoding.UTF8.GetBytes(value)));
    }

    private sealed class Person
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;

        [ProtectedPersonalData]
        public string Email { get; set; } = string.Empty;

        public string? EmailHash { get; set; }
    }

    private sealed class PeopleDbContext(
        DbContextOptions<PeopleDbContext> options,
        ICurrentTenant currentTenant,
        ICurrentUser currentUser,
        DomainEventDispatcher dispatcher,
        IServiceProvider sp)
        : ModuleDbContext(options, currentTenant, currentUser, dispatcher, sp)
    {
        protected override string TablePrefix => "people_";
        public DbSet<Person> People => Set<Person>();
    }
}
