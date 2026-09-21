using System.Diagnostics;
using System.Text;
using FluentAssertions;
using Xunit;

namespace Modulus.Cli.Integration.Tests;

/// <summary>
/// H15 — the highest-value missing test in the repo: nothing anywhere
/// verified that a solution scaffolded by <c>modulus app</c> actually
/// compiles. <c>Modulus.Cli.Tests</c> has 269 tests, none of which shell out
/// to <c>dotnet</c>; every assertion is against generated text. This test
/// packs the framework, builds the CLI, runs it end-to-end against a fresh
/// local NuGet feed, then builds the app it scaffolds -- the same sequence a
/// new user follows from the README.
///
/// Slow (multi-minute: packs 40+ projects) and needs network access for the
/// generated app's non-Modulus package restore, so this runs in CI's
/// `integration` job (`Category=Integration`), not the fast `Category=Unit`
/// suite used for local iteration.
/// </summary>
[Trait("Category", "Integration")]
public sealed class CliScaffoldBuildTests
{
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromMinutes(10);

    [Fact]
    public async Task ModulusApp_ScaffoldedSolution_BuildsSuccessfully()
    {
        var repoRoot = FindRepoRoot();
        var work = Directory.CreateTempSubdirectory("modulus-cli-e2e-");
        try
        {
            var nupkgDir = Path.Combine(work.FullName, "nupkg");
            var cliBinDir = Path.Combine(work.FullName, "cli-bin");
            var outputDir = Path.Combine(work.FullName, "output");
            Directory.CreateDirectory(nupkgDir);
            Directory.CreateDirectory(outputDir);

            // 1. Pack every Cobytelabs.Modulus.* package into a throwaway
            // local feed. CI's `integration` job does not receive the
            // `build` job's packed artifacts (separate jobs, no shared
            // artifact download), so this test must produce its own.
            await RunAsync(
                "dotnet",
                $"pack \"{Path.Combine(repoRoot, "modulus.slnx")}\" -c Release -o \"{nupkgDir}\" --nologo",
                repoRoot);

            // 2. Build the CLI itself.
            await RunAsync(
                "dotnet",
                $"build \"{Path.Combine(repoRoot, "cli", "Modulus.Cli.csproj")}\" -c Release -o \"{cliBinDir}\" --nologo",
                repoRoot);
            var cliDll = Path.Combine(cliBinDir, "Modulus.Cli.dll");
            File.Exists(cliDll).Should().BeTrue($"the CLI build should have produced {cliDll}");

            // 3. Scaffold a fresh app, pointed at the local feed. --kind api
            // and no UI modules keep the package surface minimal (no
            // `dotnet add package` shelling-out from WireUiModules); default
            // NoExample=false includes the example Catalog module so this
            // exercises real generated 4-layer module code, not just the
            // bare host. --auth none avoids OpenIddict cert/seed complexity
            // for this first smoke test.
            var scaffoldArgs = string.Join(' ',
                "app", "TestApp",
                "--kind", "api",
                "--database", "SQLite",
                "--auth", "none",
                "--message-broker", "none",
                "--caching", "inmemory",
                "--storage", "local",
                "--signalr", "none",
                "--migration-engine", "efcore",
                "--package-source", $"\"{nupkgDir}\"",
                "-o", $"\"{outputDir}\"");
            await RunAsync("dotnet", $"\"{cliDll}\" {scaffoldArgs}", work.FullName);

            var appSlnx = Path.Combine(outputDir, "TestApp", "TestApp.slnx");
            File.Exists(appSlnx).Should().BeTrue($"the CLI should have scaffolded {appSlnx}");

            // 4. Build what the CLI generated. This is the actual claim
            // under test: "modulus app gives you a working solution."
            await RunAsync("dotnet", $"build \"{appSlnx}\" -c Release --nologo", Path.Combine(outputDir, "TestApp"));
        }
        finally
        {
            try { work.Delete(recursive: true); } catch { /* best-effort cleanup */ }
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "modulus.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate modulus.slnx by walking up from {AppContext.BaseDirectory}");
    }

    private static async Task RunAsync(string fileName, string arguments, string workingDirectory)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        process.Start();
        process.StandardInput.Close(); // never let a stray prompt block the pipeline
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var cts = new CancellationTokenSource(ProcessTimeout);
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best-effort */ }
            throw new TimeoutException(
                $"'{fileName} {arguments}' in {workingDirectory} did not exit within {ProcessTimeout}.\n" +
                $"--- stdout ---\n{stdout}\n--- stderr ---\n{stderr}");
        }

        process.ExitCode.Should().Be(0,
            $"'{fileName} {arguments}' in {workingDirectory} should succeed.\n" +
            $"--- stdout ---\n{stdout}\n--- stderr ---\n{stderr}");
    }
}
