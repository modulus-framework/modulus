using FluentAssertions;
using Xunit;

namespace Modulus.Messaging.Transports.PostgreSql.Integration.Tests;

/// <summary>
/// The checked-in migration script (docs/messaging/postgresql-schema.sql) must be the script the transport runs for the
/// default schema and table, so a DBA who applies the file by hand gets the same schema. Needs no database.
/// </summary>
[Trait("Category", "Unit")]
public sealed class PostgreSqlSchemaScriptTests
{
    [Fact]
    public void The_checked_in_migration_script_matches_the_schema_the_transport_creates()
    {
        var file = Path.Combine(RepositoryRoot(), "docs", "messaging", "postgresql-schema.sql");
        var statements = StripHeader(File.ReadAllText(file));

        statements.Should().Be(StripHeader(PostgreSqlTransport.SchemaScript(new PostgreSqlTransportOptions { ConnectionString = "Host=unused" })));
    }

    // The header is comments and blank lines before the first statement; the statements must match exactly.
    private static string StripHeader(string script)
    {
        var lines = script.Replace("\r\n", "\n").Split('\n');
        var first = Array.FindIndex(lines, line => line.Length > 0 && !line.StartsWith("--", StringComparison.Ordinal));
        return string.Join("\n", lines.Skip(first)).Trim();
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "modulus.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The repository root (modulus.slnx) was not found.");
    }
}
