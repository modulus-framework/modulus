using Xunit;

namespace Modulus.Messaging.Transports.PostgreSql.Integration.Tests;

[Trait("Category", "Integration")]
public sealed class SchemaScriptTests
{
    [Fact]
    public void The_documented_schema_script_matches_the_one_the_transport_runs()
    {
        var expected = PostgreSqlTransport.SchemaScript(new PostgreSqlTransportOptions { ConnectionString = "Host=unused" });

        var documented = string.Join(
            "\n",
            File.ReadAllLines(FindDocumentedScript())
                .Where(line => !line.StartsWith("--", StringComparison.Ordinal)));

        Assert.Equal(expected.Trim(), documented.Trim());
    }

    [Fact]
    public void The_schema_script_is_additive_only()
    {
        // A rollout runs this script before the new code starts, while old instances still read the table. Dropping, renaming
        // or retyping a column would break them, so such a change needs a new table or a new column and an expand/contract plan.
        var script = PostgreSqlTransport.SchemaScript(new PostgreSqlTransportOptions { ConnectionString = "Host=unused" })
            .ToUpperInvariant();

        foreach (var forbidden in new[] { "DROP ", "RENAME ", "ALTER COLUMN", " TYPE ", "TRUNCATE", "DELETE " })
        {
            Assert.DoesNotContain(forbidden, script);
        }
    }

    private static string FindDocumentedScript()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "docs", "messaging", "postgresql-queue-schema.sql");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("docs/messaging/postgresql-queue-schema.sql was not found above the test output.");
    }
}
