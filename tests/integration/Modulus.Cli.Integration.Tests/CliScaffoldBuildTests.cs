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


    [Theory]
    [InlineData("mvc")]
    [InlineData("razor-pages")]
    [InlineData("blazor")]
    // The fluid engine is refused by the CLI for a web host (only mvc, razor-pages and blazor are generated), so it is not in the matrix.
    public async Task ModulusApp_ScaffoldedWithUiEngine_BuildsSuccessfully(string uiEngine)
    {
        var repoRoot = FindRepoRoot();
        var work = Directory.CreateTempSubdirectory("modulus-cli-ui-e2e-");
        try
        {
            var nupkgDir = Path.Combine(work.FullName, "nupkg");
            var cliBinDir = Path.Combine(work.FullName, "cli-bin");
            var outputDir = Path.Combine(work.FullName, "output");
            var isolatedCache = new Dictionary<string, string>
            {
                ["NUGET_PACKAGES"] = Path.Combine(work.FullName, "packages"),
                // The generated app builds in its own folder; a shared redirected output folder would collide between apps.
                ["ArtifactsPath"] = string.Empty,
            };
            Directory.CreateDirectory(nupkgDir);
            Directory.CreateDirectory(outputDir);

            // 1. Pack all Modulus.* packages
            await RunAsync(
                "dotnet",
                $"pack \"{Path.Combine(repoRoot, "modulus.slnx")}\" -c Release -o \"{nupkgDir}\" --nologo",
                repoRoot);

            // 2. The UI framework packages come from the modulus-ui repository. Copy them into this run's feed.
            CopyUiPackages(nupkgDir);

            // 3. Build the CLI
            await RunAsync(
                "dotnet",
                $"build \"{Path.Combine(repoRoot, "cli", "Modulus.Cli.csproj")}\" -c Release -o \"{cliBinDir}\" --nologo",
                repoRoot);
            var cliDll = Path.Combine(cliBinDir, "Modulus.Cli.dll");
            File.Exists(cliDll).Should().BeTrue($"the CLI build should have produced {cliDll}");

            // 4. Scaffold a fresh web app with UI engine
            var appName = $"TestApp{char.ToUpper(uiEngine[0])}{uiEngine.Substring(1).Replace("-", "")}";
            var scaffoldArgs = string.Join(' ',
                "app", appName,
                "--kind", "webapp+api",
                "--ui-engine", uiEngine,
                "--database", "SQLite",
                "--auth", "none",
                "--message-broker", "none",
                "--caching", "inmemory",
                "--storage", "local",
                "--signalr", "none",
                "--migration-engine", "efcore",
                "--package-source", $"\"{nupkgDir}\"",
                "-o", $"\"{outputDir}\"");
            await RunAsync("dotnet", $"\"{cliDll}\" {scaffoldArgs}", work.FullName, isolatedCache);

            var appSlnx = Path.Combine(outputDir, appName, $"{appName}.slnx");
            File.Exists(appSlnx).Should().BeTrue($"the CLI should have scaffolded {appSlnx}");

            // 4. Verify .modulus.json was created with UI engine
            var modulusJson = Path.Combine(outputDir, appName, ".modulus.json");
            File.Exists(modulusJson).Should().BeTrue($"CLI should have created {modulusJson}");
            var jsonContent = File.ReadAllText(modulusJson);
            jsonContent.Should().Contain($"\"ui_engine\": \"{uiEngine}\"", $".modulus.json should specify ui_engine: {uiEngine}");

            // 5. Generate CRUD to test UI template integration
            var generateCrudArgs = string.Join(' ',
                "generate-crud", "Product",
                "--module", "Catalog",
                "--package-source", $"\"{nupkgDir}\"");
            await RunAsync("dotnet", $"\"{cliDll}\" {generateCrudArgs}", Path.Combine(outputDir, appName), isolatedCache);

            // 6. Build the scaffolded app with generated CRUD pages
            await RunAsync("dotnet", $"build \"{appSlnx}\" -c Release --nologo", Path.Combine(outputDir, appName), isolatedCache);
        }
        finally
        {
            try { work.Delete(recursive: true); } catch { /* best-effort cleanup */ }
        }
    }

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
            // The packed version number does not change between runs, so a machine-wide package cache would serve the
            // Modulus.* packages of an earlier pack (and hide every framework change since). The generated
            // app restores into its own cache instead.
            var isolatedCache = new Dictionary<string, string>
            {
                ["NUGET_PACKAGES"] = Path.Combine(work.FullName, "packages"),
                // The generated app builds in its own folder; a shared redirected output folder would collide between apps.
                ["ArtifactsPath"] = string.Empty,
            };
            Directory.CreateDirectory(nupkgDir);
            Directory.CreateDirectory(outputDir);

            // 1. Pack every Modulus.* package into a throwaway
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
            await RunAsync("dotnet", $"\"{cliDll}\" {scaffoldArgs}", work.FullName, isolatedCache);

            var appSlnx = Path.Combine(outputDir, "TestApp", "TestApp.slnx");
            File.Exists(appSlnx).Should().BeTrue($"the CLI should have scaffolded {appSlnx}");

            // 4. Build what the CLI generated. This is the actual claim
            // under test: "modulus app gives you a working solution."
            await RunAsync("dotnet", $"build \"{appSlnx}\" -c Release --nologo", Path.Combine(outputDir, "TestApp"), isolatedCache);
        }
        finally
        {
            try { work.Delete(recursive: true); } catch { /* best-effort cleanup */ }
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(SourceDirectory());
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "modulus.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate modulus.slnx by walking up from {AppContext.BaseDirectory}");
    }

    [Fact]
    public async Task ModulusApp_ScaffoldedWithOpenIddict_BuildsAndItsTestsPass()
    {
        var repoRoot = FindRepoRoot();
        var work = Directory.CreateTempSubdirectory("modulus-cli-oidc-");
        try
        {
            var nupkgDir = Path.Combine(work.FullName, "nupkg");
            var cliBinDir = Path.Combine(work.FullName, "cli-bin");
            var outputDir = Path.Combine(work.FullName, "output");
            var isolatedCache = new Dictionary<string, string>
            {
                ["NUGET_PACKAGES"] = Path.Combine(work.FullName, "packages"),
                ["ArtifactsPath"] = string.Empty,
            };
            Directory.CreateDirectory(nupkgDir);
            Directory.CreateDirectory(outputDir);

            // 1. Pack every Modulus.* package into a local feed of this test run.
            await RunAsync(
                "dotnet",
                $"pack \"{Path.Combine(repoRoot, "modulus.slnx")}\" -c Release -o \"{nupkgDir}\" --nologo",
                repoRoot);

            // 2. Build the CLI.
            await RunAsync(
                "dotnet",
                $"build \"{Path.Combine(repoRoot, "cli", "Modulus.Cli.csproj")}\" -c Release -o \"{cliBinDir}\" --nologo",
                repoRoot);
            var cliDll = Path.Combine(cliBinDir, "Modulus.Cli.dll");
            File.Exists(cliDll).Should().BeTrue($"the CLI build should have produced {cliDll}");

            // 3. Scaffold an API with the local OpenIddict token server (the identity module, seeding and the token endpoint).
            var scaffoldArgs = string.Join(' ',
                "app", "TestOidc",
                "--kind", "api",
                "--database", "SQLite",
                "--auth", "openiddict",
                "--message-broker", "none",
                "--caching", "inmemory",
                "--storage", "local",
                "--signalr", "none",
                "--migration-engine", "efcore",
                "--package-source", $"\"{nupkgDir}\"",
                "-o", $"\"{outputDir}\"");
            await RunAsync("dotnet", $"\"{cliDll}\" {scaffoldArgs}", work.FullName, isolatedCache);

            var appSlnx = Path.Combine(outputDir, "TestOidc", "TestOidc.slnx");
            File.Exists(appSlnx).Should().BeTrue($"the CLI should have scaffolded {appSlnx}");

            // 4. Build the generated solution, then run its tests: the sign-in, the token endpoint and the generated tests.
            await RunAsync("dotnet", $"build \"{appSlnx}\" -c Release --nologo", Path.Combine(outputDir, "TestOidc"), isolatedCache);
            await RunAsync("dotnet", $"test \"{appSlnx}\" -c Release --no-build --nologo", Path.Combine(outputDir, "TestOidc"), isolatedCache);
        }
        finally
        {
            try { work.Delete(recursive: true); } catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Copies the modulus-ui packages the CLI needs (version 1.8.1, the UI version in cli/Services/UiTemplatePackage.cs) from the
    /// local UI feed (MODULUS_LOCAL_FEED, then ~/.modulus/feed) into the test's package folder. Without them the generated web app
    /// cannot restore, because the test's package source is the framework feed only.
    /// </summary>
    private static void CopyUiPackages(string destination)
    {
        const string UiVersion = "1.8.1";
        var folders = new List<string>();
        if (Environment.GetEnvironmentVariable("MODULUS_LOCAL_FEED") is { Length: > 0 } custom)
            folders.AddRange(custom.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));
        folders.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".modulus", "feed"));

        var feed = folders.FirstOrDefault(f => Directory.Exists(f) && Directory.EnumerateFiles(f, $"*.{UiVersion}.nupkg").Any());
        feed.Should().NotBeNull($"the modulus-ui {UiVersion} packages should be packed into a local feed (see the modulus-ui repository)");

        var uiPackages = new[] { "Synvro.Modulus.Ui.", "Synvro.Modulus.AspNetCore.Mvc.", "Synvro.Modulus.AspNetCore.Hosting.",
            "Synvro.Modulus.AspNetCore.Fluid.", "Synvro.Modulus.Blazor.", "Synvro.Modulus.Documents." };
        var copied = Directory.EnumerateFiles(feed!, $"*.{UiVersion}.nupkg")
            .Where(file => uiPackages.Any(prefix => Path.GetFileName(file).StartsWith(prefix, StringComparison.Ordinal)))
            .ToList();
        copied.Should().NotBeEmpty();
        foreach (var file in copied)
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
    }

    private static async Task RunAsync(
        string fileName, string arguments, string workingDirectory, IReadOnlyDictionary<string, string>? environment = null)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var (name, value) in environment ?? new Dictionary<string, string>())
            psi.Environment[name] = value;

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

    // The repository is found from this source file, not the build output, which can live outside the repository.
    private static string SourceDirectory([System.Runtime.CompilerServices.CallerFilePath] string source = "") => Path.GetDirectoryName(source)!;

}

