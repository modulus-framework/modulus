namespace Modulus.EFCore.Integration.Tests;

using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Modulus.Core.Abstractions;
using Modulus.Identity.Abstractions;
using Modulus.Identity.EntityFrameworkCore;
using MySql.Data.MySqlClient;
using Npgsql;
using Xunit;

/// <summary>
/// The employee-code rule on a fresh install (premodule 3), run on each provider: a code is unique within a company when it is set, and
/// accounts without one (NULL) never conflict. Each test gets its own new database, so the model's index is what the test checks.
/// </summary>
public abstract class EmployeeCodeFreshInstallContract : IAsyncLifetime
{
    private ModulusIdentityDbContext<ModulusUser, ModulusRole>? _db;

    /// <summary>Creates an empty database for this test and returns the options that reach it.</summary>
    protected abstract Task<DbContextOptions<ModulusIdentityDbContext<ModulusUser, ModulusRole>>> CreateEmptyDatabaseAsync();

    /// <summary>What the provider stored for the index: whether it is unique, and its filter (empty when it has none).</summary>
    protected abstract Task<(bool IsUnique, string Filter)?> IndexAsync();

    public async Task InitializeAsync()
    {
        var options = await CreateEmptyDatabaseAsync();
        _db = new ModulusIdentityDbContext<ModulusUser, ModulusRole>(options, new HostTenant());
        await _db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        if (_db is not null)
            await _db.DisposeAsync();
    }

    [Fact]
    public async Task The_fresh_install_declares_the_employee_code_index_as_unique()
    {
        var index = await IndexAsync();

        index.Should().NotBeNull("the model declares the index, so a fresh install creates it");
        index!.Value.IsUnique.Should().BeTrue();
    }

    [Fact]
    public async Task A_second_account_with_the_same_code_in_the_same_company_is_refused()
    {
        var company = Guid.NewGuid();
        await AddAsync(company, "EMP001");

        var act = () => AddAsync(company, "EMP001");

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Accounts_without_a_code_never_conflict()
    {
        var company = Guid.NewGuid();
        await AddAsync(company, null);
        await AddAsync(company, null);

        (await _db!.Users.CountAsync(u => u.TenantId == company && u.EmployeeCode == null)).Should().Be(2);
    }

    [Fact]
    public async Task The_same_code_is_allowed_in_another_company()
    {
        await AddAsync(Guid.NewGuid(), "EMP001");
        await AddAsync(Guid.NewGuid(), "EMP001");

        (await _db!.Users.CountAsync(u => u.EmployeeCode == "EMP001")).Should().Be(2);
    }

    private async Task AddAsync(Guid tenant, string? code)
    {
        _db!.Users.Add(new ModulusUser
        {
            Id = Guid.NewGuid(),
            UserName = $"user-{Guid.NewGuid():N}",
            NormalizedUserName = Guid.NewGuid().ToString("N"),
            TenantId = tenant,
            EmployeeCode = code,
        });
        await _db.SaveChangesAsync();
    }

    /// <summary>The host context: the tenant filter lets every account through.</summary>
    private sealed class HostTenant : ICurrentTenant
    {
        public Guid? TenantId => null;

        public string? TenantSlug => null;

        public bool IsAvailable => false;

        public bool IsHost => true;

        public IDisposable Change(TenantInfo? tenant) => throw new NotSupportedException();
    }
}

/// <summary>The employee-code rule on a fresh PostgreSQL install (partial unique index, <c>"EmployeeCode" IS NOT NULL</c>).</summary>
[Trait("Category", "Integration")]
public sealed class EmployeeCodePostgreSqlFreshInstallTests(PostgreSqlFixture fixture) : EmployeeCodeFreshInstallContract, IClassFixture<PostgreSqlFixture>
{
    private readonly string _database = $"emp_{Guid.NewGuid():N}"[..24];

    protected override async Task<DbContextOptions<ModulusIdentityDbContext<ModulusUser, ModulusRole>>> CreateEmptyDatabaseAsync()
    {
        await using (var admin = new NpgsqlConnection(fixture.ConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{_database}\"", admin);
            await create.ExecuteNonQueryAsync();
        }

        var cs = new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Database = _database }.ToString();
        return new DbContextOptionsBuilder<ModulusIdentityDbContext<ModulusUser, ModulusRole>>().UseNpgsql(cs).Options;
    }

    protected override async Task<(bool IsUnique, string Filter)?> IndexAsync()
    {
        var cs = new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Database = _database }.ToString();
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT indexdef FROM pg_indexes WHERE indexname = @n", conn);
        cmd.Parameters.AddWithValue("n", ModulusIdentityDbContext<ModulusUser, ModulusRole>.EmployeeCodeIndexName);
        if (await cmd.ExecuteScalarAsync() is not string definition)
            return null;

        return (definition.Contains("UNIQUE INDEX", StringComparison.Ordinal), definition.Contains("IS NOT NULL", StringComparison.Ordinal) ? "IS NOT NULL" : "");
    }
}

/// <summary>The employee-code rule on a fresh SQL Server install (filtered unique index, <c>[EmployeeCode] IS NOT NULL</c>).</summary>
[Trait("Category", "Integration")]
public sealed class EmployeeCodeSqlServerFreshInstallTests(SqlServerAuditFixture fixture) : EmployeeCodeFreshInstallContract, IClassFixture<SqlServerAuditFixture>
{
    private readonly string _database = $"emp_{Guid.NewGuid():N}"[..24];

    protected override async Task<DbContextOptions<ModulusIdentityDbContext<ModulusUser, ModulusRole>>> CreateEmptyDatabaseAsync()
    {
        await using (var admin = new SqlConnection(fixture.ConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new SqlCommand($"CREATE DATABASE [{_database}]", admin);
            await create.ExecuteNonQueryAsync();
        }

        var cs = new SqlConnectionStringBuilder(fixture.ConnectionString) { InitialCatalog = _database }.ToString();
        return new DbContextOptionsBuilder<ModulusIdentityDbContext<ModulusUser, ModulusRole>>().UseSqlServer(cs).Options;
    }

    protected override async Task<(bool IsUnique, string Filter)?> IndexAsync()
    {
        var cs = new SqlConnectionStringBuilder(fixture.ConnectionString) { InitialCatalog = _database }.ToString();
        await using var conn = new SqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(
            "SELECT is_unique, ISNULL(filter_definition, '') FROM sys.indexes WHERE name = @n AND object_id = OBJECT_ID('AspNetUsers')", conn);
        cmd.Parameters.AddWithValue("@n", ModulusIdentityDbContext<ModulusUser, ModulusRole>.EmployeeCodeIndexName);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        return (reader.GetBoolean(0), reader.GetString(1));
    }
}

/// <summary>The employee-code rule on a fresh MySQL install (a unique index without a filter: MySQL allows any number of NULLs in one).</summary>
[Trait("Category", "Integration")]
public sealed class EmployeeCodeMySqlFreshInstallTests(MySqlAuditFixture fixture) : EmployeeCodeFreshInstallContract, IClassFixture<MySqlAuditFixture>
{
    private readonly string _database = $"emp_{Guid.NewGuid():N}"[..24];

    private string Connection(string database) => $"Server={fixture.Host};Port={fixture.Port};Database={database};User=root;Password=root;";

    protected override async Task<DbContextOptions<ModulusIdentityDbContext<ModulusUser, ModulusRole>>> CreateEmptyDatabaseAsync()
    {
        await using (var admin = new MySqlConnection(Connection("mysql")))
        {
            await admin.OpenAsync();
            await using var create = new MySqlCommand($"CREATE DATABASE `{_database}`", admin);
            await create.ExecuteNonQueryAsync();
        }

        return new DbContextOptionsBuilder<ModulusIdentityDbContext<ModulusUser, ModulusRole>>().UseMySQL(Connection(_database)).Options;
    }

    protected override async Task<(bool IsUnique, string Filter)?> IndexAsync()
    {
        await using var conn = new MySqlConnection(Connection(_database));
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT MIN(NON_UNIQUE) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND INDEX_NAME = @n", conn);
        cmd.Parameters.AddWithValue("@n", ModulusIdentityDbContext<ModulusUser, ModulusRole>.EmployeeCodeIndexName);
        var nonUnique = await cmd.ExecuteScalarAsync();
        if (nonUnique is null or DBNull)
            return null;

        return (Convert.ToInt32(nonUnique) == 0, "");
    }
}
