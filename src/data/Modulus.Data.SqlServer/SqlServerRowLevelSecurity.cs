namespace Modulus.Data.SqlServer;

using System.Data;
using System.Data.Common;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Modulus.Core.Abstractions.Entities;
using Modulus.EntityFrameworkCore;
using Modulus.EntityFrameworkCore.Isolation;

/// <summary>
/// Company isolation enforced by SQL Server itself: every connection carries the ambient tenant in
/// <c>SESSION_CONTEXT</c> (<c>modulus.tenant_id</c>, <c>modulus.is_host</c>), and a security policy per tenant
/// table adds a <c>FILTER</c> predicate (reads, updates, deletes) and <c>BLOCK</c> predicates (inserts and updates
/// that would land in another tenant). Raw SQL, <c>IgnoreQueryFilters()</c> and a forgotten filter all stay
/// inside the company.
/// </summary>
/// <remarks>
/// <para>
/// Turn it on per module context with <see cref="AddSqlServerRowLevelSecurity{TContext}"/> and create the
/// predicate function and policies in a migration (<see cref="EnableTenantRowLevelSecurity"/>) or at startup
/// (<see cref="EnsureAsync"/>).
/// </para>
/// <para>
/// Members of <c>db_owner</c> and <c>sysadmin</c> are still filtered by a security policy, but they can alter or
/// drop it: the application must connect as a login without <c>ALTER ANY SECURITY POLICY</c> and without
/// ownership of the <c>modulus</c> schema (see <c>docs/security/database-roles.md</c>). No tenant in scope (and
/// not the host) matches no rows. Pooled connections are reset (<c>sp_reset_connection</c>) before reuse, which
/// clears the session context.
/// </para>
/// </remarks>
public static class SqlServerRowLevelSecurity
{
    /// <summary>The session context key holding the tenant id (<c>uniqueidentifier</c>, NULL for host and nobody).</summary>
    public const string TenantKey = "modulus.tenant_id";

    /// <summary>The session context key that is 1 in the host (all-tenants) context.</summary>
    public const string HostKey = "modulus.is_host";

    /// <summary>The schema holding the predicate function and the policies.</summary>
    public const string Schema = "modulus";

    /// <summary>The inline predicate function shared by every policy.</summary>
    public const string PredicateFunction = "fn_tenant_predicate";

    /// <summary>The shared session interceptor.</summary>
    public static TenantSessionInterceptor Interceptor { get; } = new SqlServerTenantSessionInterceptor();

    /// <summary>
    /// Writes the ambient tenant into every SQL Server session of <typeparamref name="TContext"/>, so the
    /// security policies apply. Call next to <c>AddSqlServerDatabase&lt;TContext&gt;</c>.
    /// </summary>
    /// <typeparam name="TContext">The module context.</typeparam>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddSqlServerRowLevelSecurity<TContext>(this IServiceCollection services)
        where TContext : ModuleDbContext
        => services.AddTenantSessionContext<TContext>(Interceptor);

    /// <summary>Adds the tenant security policy to <paramref name="table"/> in a migration (creates the function once).</summary>
    /// <param name="migrationBuilder">The migration.</param>
    /// <param name="table">The table name, as created (with the module's prefix).</param>
    /// <param name="tenantColumn">The tenant column.</param>
    /// <param name="schema">The table's schema.</param>
    public static MigrationBuilder EnableTenantRowLevelSecurity(
        this MigrationBuilder migrationBuilder, string table, string tenantColumn = "TenantId", string schema = "dbo")
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        foreach (var statement in FunctionStatements())
            migrationBuilder.Sql(statement);
        migrationBuilder.Sql(DropPolicyStatement(table, schema));
        migrationBuilder.Sql(CreatePolicyStatement(table, tenantColumn, schema));
        return migrationBuilder;
    }

    /// <summary>Removes the tenant security policy from <paramref name="table"/> (the <c>Down</c> step).</summary>
    /// <param name="migrationBuilder">The migration.</param>
    /// <param name="table">The table name.</param>
    /// <param name="schema">The table's schema.</param>
    public static MigrationBuilder DisableTenantRowLevelSecurity(
        this MigrationBuilder migrationBuilder, string table, string schema = "dbo")
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.Sql(DropPolicyStatement(table, schema));
        return migrationBuilder;
    }

    /// <summary>
    /// The batches that create the <c>modulus</c> schema and the predicate function when missing. Each must run as
    /// its own batch (<c>CREATE FUNCTION</c> must be first in a batch, hence the <c>EXEC</c>).
    /// </summary>
    public static IReadOnlyList<string> FunctionStatements() =>
    [
        $"IF SCHEMA_ID(N'{Schema}') IS NULL EXEC(N'CREATE SCHEMA [{Schema}]');",
        $"""
        IF OBJECT_ID(N'[{Schema}].[{PredicateFunction}]', N'IF') IS NULL
            EXEC(N'CREATE FUNCTION [{Schema}].[{PredicateFunction}](@TenantId uniqueidentifier)
            RETURNS TABLE WITH SCHEMABINDING AS RETURN
                SELECT 1 AS allowed
                WHERE CAST(SESSION_CONTEXT(N''{HostKey}'') AS bit) = 1
                   OR @TenantId = CAST(SESSION_CONTEXT(N''{TenantKey}'') AS uniqueidentifier)');
        """,
    ];

    /// <summary>The statement that creates the security policy of one table.</summary>
    /// <param name="table">The table name.</param>
    /// <param name="tenantColumn">The tenant column.</param>
    /// <param name="schema">The table's schema.</param>
    public static string CreatePolicyStatement(string table, string tenantColumn = "TenantId", string schema = "dbo")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(table);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantColumn);
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);
        var target = $"{Quote(schema)}.{Quote(table)}";
        var predicate = $"{Quote(Schema)}.{Quote(PredicateFunction)}({Quote(tenantColumn)})";
        return $"""
            CREATE SECURITY POLICY {PolicyName(table, schema)}
                ADD FILTER PREDICATE {predicate} ON {target},
                ADD BLOCK PREDICATE {predicate} ON {target} AFTER INSERT,
                ADD BLOCK PREDICATE {predicate} ON {target} AFTER UPDATE
                WITH (STATE = ON);
            """;
    }

    /// <summary>The statement that drops the security policy of one table when it exists.</summary>
    /// <param name="table">The table name.</param>
    /// <param name="schema">The table's schema.</param>
    public static string DropPolicyStatement(string table, string schema = "dbo")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(table);
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);
        return $"DROP SECURITY POLICY IF EXISTS {PolicyName(table, schema)};";
    }

    /// <summary>Every statement needed for the <see cref="IHasTenantId"/> tables of <paramref name="model"/>, one batch each.</summary>
    /// <param name="model">A module context's model.</param>
    public static IReadOnlyList<string> Statements(IModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var tables = TenantTables(model).ToList();
        if (tables.Count == 0)
            return [];
        var statements = new List<string>(FunctionStatements());
        foreach (var (table, schema, column) in tables)
        {
            statements.Add(DropPolicyStatement(table, schema));
            statements.Add(CreatePolicyStatement(table, column, schema));
        }

        return statements;
    }

    /// <summary>
    /// Applies <see cref="Statements"/> to the context's database: for apps that create their schema with
    /// <c>EnsureCreated</c>, or as a startup step after migrating. Needs a login that may create schemas, functions
    /// and security policies (the migration login).
    /// </summary>
    /// <param name="context">The module context.</param>
    /// <param name="ct">Cancellation.</param>
    public static async Task EnsureAsync(DbContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        using (CrossTenantSql.Allow("Applying the SQL Server tenant security policies"))
        {
            foreach (var statement in Statements(context.Model))
                await context.Database.ExecuteSqlRawAsync(statement, ct).ConfigureAwait(false);
        }
    }

    internal static IEnumerable<(string Table, string Schema, string Column)> TenantTables(IModel model)
    {
        foreach (var entity in model.GetEntityTypes())
        {
            if (!typeof(IHasTenantId).IsAssignableFrom(entity.ClrType) || entity.GetTableName() is not { } table)
                continue;
            var schema = entity.GetSchema();
            var column = entity.FindProperty(nameof(IHasTenantId.TenantId))
                ?.GetColumnName(StoreObjectIdentifier.Table(table, schema)) ?? nameof(IHasTenantId.TenantId);
            yield return (table, schema ?? "dbo", column);
        }
    }

    private static string PolicyName(string table, string schema) => $"{Quote(Schema)}.{Quote($"tenant_{schema}_{table}")}";

    private static string Quote(string identifier) => "[" + identifier.Replace("]", "]]", StringComparison.Ordinal) + "]";

    private sealed class SqlServerTenantSessionInterceptor : TenantSessionInterceptor
    {
        protected override bool Supports(DbConnection connection) => connection is SqlConnection;

        protected override void Configure(DbCommand command, TenantSession session)
        {
            command.CommandText =
                $"EXEC sp_set_session_context @key = N'{TenantKey}', @value = @tenant; "
                + $"EXEC sp_set_session_context @key = N'{HostKey}', @value = @host;";
            command.Parameters.Add(new SqlParameter("@tenant", SqlDbType.UniqueIdentifier)
            {
                Value = session.TenantId is { } id ? id : DBNull.Value,
            });
            command.Parameters.Add(new SqlParameter("@host", SqlDbType.Bit) { Value = session.IsHost });
        }
    }
}
