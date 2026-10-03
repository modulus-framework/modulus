namespace Modulus.Data.PostgreSQL;

using System.Data.Common;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Modulus.Core.Abstractions.Entities;
using Modulus.EntityFrameworkCore;
using Modulus.EntityFrameworkCore.Isolation;
using Npgsql;

/// <summary>
/// Company isolation enforced by PostgreSQL itself: every connection carries the ambient tenant
/// (<c>modulus.tenant_id</c>, <c>modulus.is_host</c>), and a <c>FORCE</c>d row-level security policy on each tenant
/// table limits reads and writes to that tenant. Raw SQL, <c>IgnoreQueryFilters()</c> and a forgotten filter all
/// stay inside the company.
/// </summary>
/// <remarks>
/// <para>
/// Turn it on per module context with <see cref="AddPostgreSqlRowLevelSecurity{TContext}"/>, and create the
/// policies in a migration (<see cref="EnableTenantRowLevelSecurity"/>) or at startup (<see cref="EnsureAsync"/>).
/// </para>
/// <para>
/// The application must connect as a role that does not own the tables and is not a superuser and has no
/// <c>BYPASSRLS</c>: those skip row-level security. A separate migration role owns the schema (see
/// <c>docs/security/database-roles.md</c>). No tenant in scope (and not the host) matches no rows.
/// </para>
/// </remarks>
public static class PostgreSqlRowLevelSecurity
{
    /// <summary>The session setting holding the tenant id (empty for the host and for nobody).</summary>
    public const string TenantSetting = "modulus.tenant_id";

    /// <summary>The session setting that is <c>on</c> in the host (all-tenants) context.</summary>
    public const string HostSetting = "modulus.is_host";

    /// <summary>The name of the policy created on every tenant table.</summary>
    public const string PolicyName = "modulus_tenant";

    /// <summary>The shared session interceptor.</summary>
    public static TenantSessionInterceptor Interceptor { get; } = new PostgreSqlTenantSessionInterceptor();

    /// <summary>
    /// Writes the ambient tenant into every PostgreSQL session of <typeparamref name="TContext"/>, so the
    /// row-level security policies apply. Call next to <c>AddPostgreSQLDatabase&lt;TContext&gt;</c>.
    /// </summary>
    /// <typeparam name="TContext">The module context.</typeparam>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddPostgreSqlRowLevelSecurity<TContext>(this IServiceCollection services)
        where TContext : ModuleDbContext
        => services.AddTenantSessionContext<TContext>(Interceptor);

    /// <summary>Adds the tenant policy to <paramref name="table"/> in a migration.</summary>
    /// <param name="migrationBuilder">The migration.</param>
    /// <param name="table">The table name, as created (with the module's prefix).</param>
    /// <param name="tenantColumn">The tenant column (<c>tenant_id</c> with the default snake-case naming).</param>
    /// <param name="schema">The schema, or null for the search path.</param>
    public static MigrationBuilder EnableTenantRowLevelSecurity(
        this MigrationBuilder migrationBuilder, string table, string tenantColumn = "tenant_id", string? schema = null)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.Sql(EnableScript(table, tenantColumn, schema));
        return migrationBuilder;
    }

    /// <summary>Removes the tenant policy from <paramref name="table"/> in a migration (the <c>Down</c> step).</summary>
    /// <param name="migrationBuilder">The migration.</param>
    /// <param name="table">The table name.</param>
    /// <param name="schema">The schema, or null for the search path.</param>
    public static MigrationBuilder DisableTenantRowLevelSecurity(
        this MigrationBuilder migrationBuilder, string table, string? schema = null)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.Sql(DisableScript(table, schema));
        return migrationBuilder;
    }

    /// <summary>The statements that enable the tenant policy on one table (idempotent).</summary>
    /// <param name="table">The table name.</param>
    /// <param name="tenantColumn">The tenant column.</param>
    /// <param name="schema">The schema, or null.</param>
    public static string EnableScript(string table, string tenantColumn = "tenant_id", string? schema = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(table);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantColumn);
        var name = Qualified(table, schema);
        var predicate =
            $"current_setting('{HostSetting}', true) = 'on' OR {Quote(tenantColumn)} = nullif(current_setting('{TenantSetting}', true), '')::uuid";
        return $"""
            ALTER TABLE {name} ENABLE ROW LEVEL SECURITY;
            ALTER TABLE {name} FORCE ROW LEVEL SECURITY;
            DROP POLICY IF EXISTS {PolicyName} ON {name};
            CREATE POLICY {PolicyName} ON {name} USING ({predicate}) WITH CHECK ({predicate});
            """;
    }

    /// <summary>The statements that remove the tenant policy from one table.</summary>
    /// <param name="table">The table name.</param>
    /// <param name="schema">The schema, or null.</param>
    public static string DisableScript(string table, string? schema = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(table);
        var name = Qualified(table, schema);
        return $"""
            DROP POLICY IF EXISTS {PolicyName} ON {name};
            ALTER TABLE {name} NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE {name} DISABLE ROW LEVEL SECURITY;
            """;
    }

    /// <summary>The enable statements for every <see cref="IHasTenantId"/> table of <paramref name="model"/>.</summary>
    /// <param name="model">A module context's model.</param>
    public static string Script(IModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var sql = new StringBuilder();
        foreach (var (table, schema, column) in TenantTables(model))
            sql.AppendLine(EnableScript(table, column, schema));
        return sql.ToString();
    }

    /// <summary>
    /// Applies <see cref="Script"/> to the context's database: for apps that create their schema with
    /// <c>EnsureCreated</c>, or as a startup step after migrating. Runs as the connection's role, so it needs the
    /// table owner (the migration role).
    /// </summary>
    /// <param name="context">The module context.</param>
    /// <param name="ct">Cancellation.</param>
    public static async Task EnsureAsync(DbContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var script = Script(context.Model);
        if (script.Length == 0)
            return;
        using (CrossTenantSql.Allow("Applying the PostgreSQL tenant row-level security policies"))
            await context.Database.ExecuteSqlRawAsync(script, ct).ConfigureAwait(false);
    }

    internal static IEnumerable<(string Table, string? Schema, string Column)> TenantTables(IModel model)
    {
        foreach (var entity in model.GetEntityTypes())
        {
            if (!typeof(IHasTenantId).IsAssignableFrom(entity.ClrType) || entity.GetTableName() is not { } table)
                continue;
            var schema = entity.GetSchema();
            var column = entity.FindProperty(nameof(IHasTenantId.TenantId))
                ?.GetColumnName(StoreObjectIdentifier.Table(table, schema)) ?? "tenant_id";
            yield return (table, schema, column);
        }
    }

    private static string Qualified(string table, string? schema)
        => schema is null ? Quote(table) : $"{Quote(schema)}.{Quote(table)}";

    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private sealed class PostgreSqlTenantSessionInterceptor : TenantSessionInterceptor
    {
        protected override bool Supports(DbConnection connection) => connection is NpgsqlConnection;

        protected override void Configure(DbCommand command, TenantSession session)
        {
            command.CommandText = $"SELECT set_config('{TenantSetting}', @tenant, false), set_config('{HostSetting}', @host, false)";
            Add(command, "tenant", session.TenantId?.ToString("D") ?? string.Empty);
            Add(command, "host", session.IsHost ? "on" : "off");
        }

        private static void Add(DbCommand command, string name, string value)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }
    }
}
