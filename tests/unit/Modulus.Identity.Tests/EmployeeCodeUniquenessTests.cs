namespace Modulus.Identity.Tests;

using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Modulus.Core.Abstractions;
using Modulus.Identity.Abstractions;
using Modulus.Identity.EntityFrameworkCore;
using Xunit;

/// <summary>
/// The employee-code rule in the database (premodule 3): within a company a code is unique when it is set; accounts without one
/// (NULL) do not conflict. A fresh install gets the constraint from the model, and this test checks the constraint on SQLite, which
/// supports the same partial index. The other providers are covered by the Testcontainers tests in Modulus.EFCore.Integration.Tests.
/// </summary>
[Trait("Category", "Unit")]
public sealed class EmployeeCodeUniquenessTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly ModulusIdentityDbContext<ModulusUser, ModulusRole> _db;

    public EmployeeCodeUniquenessTests()
    {
        _connection.Open();
        var options = new DbContextOptionsBuilder<ModulusIdentityDbContext<ModulusUser, ModulusRole>>().UseSqlite(_connection).Options;
        _db = new ModulusIdentityDbContext<ModulusUser, ModulusRole>(options, new NoTenant());
        _db.Database.EnsureCreated();
    }

    [Fact]
    public void The_fresh_install_creates_the_employee_code_index_as_unique_and_filtered_on_sqlite()
    {
        var sql = _connection.CreateCommand();
        sql.CommandText = "SELECT sql FROM sqlite_master WHERE type = 'index' AND name = @name";
        sql.Parameters.AddWithValue("@name", ModulusIdentityDbContext<ModulusUser, ModulusRole>.EmployeeCodeIndexName);

        var definition = (string?)sql.ExecuteScalar();

        definition.Should().NotBeNull();
        definition.Should().Contain("UNIQUE");
        definition.Should().Contain("\"EmployeeCode\" IS NOT NULL");
    }

    [Fact]
    public async Task A_second_account_with_the_same_code_in_the_same_company_is_refused_by_the_database()
    {
        var company = Guid.NewGuid();
        await AddAsync(company, "EMP001");

        var act = () => AddAsync(company, "EMP001");

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Accounts_without_a_code_do_not_conflict_with_each_other()
    {
        var company = Guid.NewGuid();
        await AddAsync(company, null);
        await AddAsync(company, null);

        (await _db.Users.CountAsync(u => u.TenantId == company && u.EmployeeCode == null)).Should().Be(2);
    }

    [Fact]
    public async Task The_same_code_is_allowed_in_another_company()
    {
        await AddAsync(Guid.NewGuid(), "EMP001");
        await AddAsync(Guid.NewGuid(), "EMP001");

        (await _db.Users.CountAsync(u => u.EmployeeCode == "EMP001")).Should().Be(2);
    }

    /// <summary>
    /// The rule for case and whitespace: a code is trimmed and upper-cased by <c>ProfileRules.NormalizeEmployeeCode</c> and
    /// <c>ShopFloorRules.NormalizeCode</c> before it is written, so the stored value is one spelling and the constraint compares that.
    /// Writing "emp001" after "EMP001" therefore collides once it is normalized, which the application does on every write path.
    /// </summary>
    [Theory]
    [InlineData("  emp001 ")]
    [InlineData("Emp001")]
    public void Both_normalizers_map_a_spelling_of_a_code_to_the_stored_upper_case_form(string spelling)
    {
        ProfileRules.NormalizeEmployeeCode(spelling.Trim()).Should().Be("EMP001");
        ShopFloorRules.NormalizeCode(spelling).Should().Be("EMP001");
    }

    private async Task AddAsync(Guid tenant, string? code)
    {
        _db.Users.Add(new ModulusUser
        {
            Id = Guid.NewGuid(),
            UserName = $"user-{Guid.NewGuid():N}",
            NormalizedUserName = Guid.NewGuid().ToString("N"),
            TenantId = tenant,
            EmployeeCode = code,
        });
        await _db.SaveChangesAsync();
    }

    /// <summary>The host context: the query filter lets every account through, so the test sees the rows it wrote.</summary>
    private sealed class NoTenant : ICurrentTenant
    {
        public Guid? TenantId => null;

        public string? TenantSlug => null;

        public bool IsAvailable => false;

        public bool IsHost => true;

        public IDisposable Change(TenantInfo? tenant) => throw new NotSupportedException();
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
