using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Reflection;

namespace Modulus.BackgroundJobs.Quartz;

/// <summary>
/// The Quartz.NET ADO.NET job store schema that Modulus ships. The Quartz package carries no SQL, so the tables come
/// from here. Apply the script once per database, before the scheduler starts.
/// </summary>
public static class QuartzSchema
{
    private const string SqliteResource = "Modulus.BackgroundJobs.Quartz.Schema.quartz-sqlite.sql";
    private const string PostgresResource = "Modulus.BackgroundJobs.Quartz.Schema.quartz-postgres.sql";
    private const string SqlServerResource = "Modulus.BackgroundJobs.Quartz.Schema.quartz-sqlserver.sql";

    // The job store has 11 tables: JOB_DETAILS, TRIGGERS, the four trigger-type tables, CALENDARS, PAUSED_TRIGGER_GRPS,
    // FIRED_TRIGGERS, SCHEDULER_STATE and LOCKS.
    private const int TableCount = 11;

    private const string SqliteTableQuery =
        "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name LIKE 'QRTZ\\_%' ESCAPE '\\'";

    private const string SqlServerTableQuery =
        "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = SCHEMA_NAME() AND TABLE_NAME LIKE 'QRTZ[_]%'";

    private const string PostgresTableQuery =
        @"SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = current_schema() AND table_name LIKE 'qrtz\_%'";

    /// <summary>The SQLite script (tables, indexes and the lock rows Quartz needs).</summary>
    public static string SqliteScript => ReadEmbedded(SqliteResource);

    /// <summary>The PostgreSQL script (tables and the lock rows Quartz needs).</summary>
    public static string PostgresScript => ReadEmbedded(PostgresResource);

    /// <summary>The SQL Server script (tables, constraints and the lock rows Quartz needs).</summary>
    public static string SqlServerScript => ReadEmbedded(SqlServerResource);

    /// <summary>
    /// Creates the SQLite job store tables when they are missing and does nothing when they already exist, so it is
    /// safe to call on every start. A database with some but not all of the tables fails rather than being guessed at.
    /// </summary>
    public static Task CreateSqliteAsync(DbConnection connection, CancellationToken ct = default) =>
        CreateAsync(connection, SqliteScript, SqliteTableQuery, ct);

    /// <summary>
    /// Creates the PostgreSQL job store tables in the current schema. Same rules as <see cref="CreateSqliteAsync"/>.
    /// </summary>
    public static Task CreatePostgresAsync(DbConnection connection, CancellationToken ct = default) =>
        CreateAsync(connection, PostgresScript, PostgresTableQuery, ct);

    /// <summary>
    /// Creates the SQL Server job store tables in the default schema of the connection. Same rules as <see cref="CreateSqliteAsync"/>.
    /// </summary>
    public static Task CreateSqlServerAsync(DbConnection connection, CancellationToken ct = default) =>
        CreateAsync(connection, SqlServerScript, SqlServerTableQuery, ct);

    private static async Task CreateAsync(DbConnection connection, string script, string tableQuery, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(ct);

        var existing = await CountTablesAsync(connection, tableQuery, ct);
        if (existing == 0)
        {
            foreach (var statement in StripComments(script).Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var sql = statement.Trim();
                if (sql.Length == 0)
                    continue;

                await using var command = connection.CreateCommand();
                command.CommandText = sql;
                await command.ExecuteNonQueryAsync(ct);
            }

            return;
        }

        if (existing != TableCount)
            throw new InvalidOperationException(
                $"The database has {existing} of the {TableCount} Quartz job store tables. Repair it by hand; Modulus will not create the rest.");
    }

    private static async Task<int> CountTablesAsync(DbConnection connection, string tableQuery, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = tableQuery;
        var value = await command.ExecuteScalarAsync(ct);
        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private static string StripComments(string script) =>
        string.Join('\n', script
            .Split('\n')
            .Where(line => !line.TrimStart().StartsWith("--", StringComparison.Ordinal)));

    private static string ReadEmbedded(string resource)
    {
        using var stream = typeof(QuartzSchema).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Embedded resource '{resource}' is missing from {Assembly.GetExecutingAssembly().GetName().Name}.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
