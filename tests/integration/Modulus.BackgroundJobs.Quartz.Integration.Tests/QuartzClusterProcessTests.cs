namespace Modulus.BackgroundJobs.Quartz.Integration.Tests;

using System.Diagnostics;
using FluentAssertions;
using Npgsql;
using Xunit;

/// <summary>
/// Two clustered nodes in separate processes share one PostgreSQL job store. Every one-shot job must fire exactly once.
/// Each node is a process of its own, because Quartz keeps one scheduler per name per process.
/// </summary>
[Collection(QuartzPostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class QuartzClusterProcessTests(QuartzPostgresFixture fixture)
{
    private const int JobCount = 20;

    [Fact]
    public async Task Each_scheduled_job_fires_exactly_once_across_two_clustered_processes()
    {
        var schema = "q" + Guid.NewGuid().ToString("N");
        await CreateSchemaAsync(schema);
        var connection = ConnectionStringFor(schema);

        var scheduling = await RunAsync("schedule", connection, JobCount.ToString(), "8");
        scheduling.ExitCode.Should().Be(0, scheduling.Error);
        var scheduled = Lines(scheduling.Output, "SCHEDULED ").ToList();
        scheduled.Should().HaveCount(JobCount);

        // Both nodes run at the same time, so they compete for the same due triggers.
        var nodeA = StartAsync("run", connection, "node-a", "20");
        var nodeB = StartAsync("run", connection, "node-b", "20");
        var results = await Task.WhenAll(nodeA, nodeB);
        foreach (var result in results)
            result.ExitCode.Should().Be(0, result.Error);

        var fired = results.SelectMany(r => Lines(r.Output, "FIRED ")).ToList();

        fired.Should().HaveCount(JobCount, "every job fires once and no more");
        fired.Distinct().Should().HaveCount(JobCount, "no job fires on two nodes");
        fired.Should().BeEquivalentTo(scheduled, "every scheduled job fires");
    }

    // The run id is the second word of each line; the node id that follows it is ignored here.
    private static IEnumerable<string> Lines(string output, string prefix)
        => output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.StartsWith(prefix, StringComparison.Ordinal))
            .Select(l => l[prefix.Length..].Split(' ')[0]);

    private string ConnectionStringFor(string schema) =>
        new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { SearchPath = schema }.ConnectionString;

    private async Task CreateSchemaAsync(string schema)
    {
        await using (var admin = new NpgsqlConnection(fixture.ConnectionString))
        {
            await admin.OpenAsync();
            await using var create = admin.CreateCommand();
            create.CommandText = $"CREATE SCHEMA {schema}";
            await create.ExecuteNonQueryAsync();
        }

        // The tables must go into the new schema, so this connection's search path names it.
        await using var connection = new NpgsqlConnection(ConnectionStringFor(schema));
        await connection.OpenAsync();
        await QuartzSchema.CreatePostgresAsync(connection);
    }

    private static Task<ChildResult> RunAsync(params string[] arguments) => StartAsync(arguments);

    private static Task<ChildResult> StartAsync(params string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(ClusterNodeAssembly());
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        return RunProcessAsync(start);
    }

    private static async Task<ChildResult> RunProcessAsync(ProcessStartInfo start)
    {
        using var process = Process.Start(start) ?? throw new InvalidOperationException("The node process did not start.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await process.WaitForExitAsync(timeout.Token);
        return new ChildResult(process.ExitCode, await output, await error);
    }

    private static string ClusterNodeAssembly()
    {
        const string Dll = "Modulus.BackgroundJobs.Quartz.ClusterNode.dll";
        var output = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);

        // The copy in this output has the node's dependencies when the build output is in the default place.
        if (File.Exists(Path.Combine(output, "Microsoft.Extensions.DependencyInjection.dll")))
            return Path.Combine(output, Dll);

        // With the build output redirected, the copy can lack dependencies, so the node runs from its own build folder: under
        // the same bin folder, the node's project folder with the same framework folder name.
        var framework = Path.GetFileName(output);
        var binFolder = Path.GetDirectoryName(Path.GetDirectoryName(output)!)!;
        return Path.Combine(binFolder, "Modulus.BackgroundJobs.Quartz.ClusterNode", framework, Dll);
    }

    private sealed record ChildResult(int ExitCode, string Output, string Error);
}
