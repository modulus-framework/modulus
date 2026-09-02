using System.ComponentModel;
using Modulus.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Modulus.Cli.Commands;

/// <summary>
/// Adds a layered module to an existing application and wires it into
/// the host Program.cs's AddModulus registration and the Host project's
/// ProjectReferences.
/// </summary>
internal sealed class AddModuleCommand : Command<AddModuleCommand.Settings>
{
    internal sealed class Settings : ModulusSettings
    {
        [Description("Name of the module to add (e.g. Orders, Billing). Omit to be prompted.")]
        [CommandArgument(0, "[name]")]
        public string? Name { get; init; }

        [Description("Database provider: SQLite (default), SqlServer, PostgreSQL, MySQL")]
        [CommandOption("-d|--database")]
        public string? Database { get; init; }

        [Description("Migration engine: efcore (default), dbsh. Omit to inherit from the app's existing modules.")]
        [CommandOption("--migration-engine")]
        public string? MigrationEngine { get; init; }
    }

    public override int Execute(CommandContext ctx, Settings s)
    {
        s.Apply();
        return CommandRunner.Run(() => ExecuteCore(ctx, s));
    }

    private int ExecuteCore(CommandContext ctx, Settings s)
    {
        var slnx = SolutionHelper.FindSolution(Environment.CurrentDirectory)
            ?? throw new InvalidOperationException(
                "No .slnx file found in the current directory tree. " +
                "Run this command from within a Modulus application.");

        var solutionDir = Path.GetDirectoryName(slnx)!;
        var rootNs = DetectRootNamespace(solutionDir)
            ?? Path.GetFileNameWithoutExtension(slnx);

        var moduleName = !string.IsNullOrWhiteSpace(s.Name)
            ? CodeGen.ValidateIdentifier(s.Name, "Module")
            : Ux.AskRequired("Module name [grey](e.g. Orders, Billing)[/]:",
                ciHint: "Pass the module name, e.g. `modulus add-module Orders`.");

        var moduleNs = $"{rootNs}.Modules.{moduleName}";
        var projectDir = Path.Combine(solutionDir, "src", "Modules", moduleNs);

        if (Directory.Exists(projectDir) && Directory.EnumerateFileSystemEntries(projectDir).Any())
        {
            if (!Ux.Confirm($"Module [cyan]{moduleName}[/] already exists. Overwrite?", nonInteractiveDefault: false))
            {
                Ux.Error("Aborted.");
                return 1;
            }
            Ux.DeleteDirectory(projectDir);
        }

        var database = string.IsNullOrWhiteSpace(s.Database)
            ? Ux.SelectOrFallback("Database provider?", NewAppCommand.KnownProviders, "SQLite")
            : NewAppCommand.ValidateChoice(
                s.Database!, NewAppCommand.KnownProviders, "database provider");

        // Explicit flag wins; otherwise inherit the app's prevailing engine
        // (dbsh when every existing module uses dbsh, else efcore).
        var migrationEngine = string.IsNullOrWhiteSpace(s.MigrationEngine)
            ? MigrateSupport.DetectEngine(solutionDir)
            : NewAppCommand.ValidateChoice(
                s.MigrationEngine!, NewAppCommand.KnownMigrationEngines, "migration engine");

        var model = new ModuleModel
        {
            RootNamespace = rootNs,
            ModuleName = moduleName,
            ModuleNamespace = moduleNs,
            DbProvider = database,
            MigrationEngine = migrationEngine,
        };

        // Generate the layered module skeleton.
        var appCmd = new NewAppCommand();
        Ux.Status($"Scaffolding {moduleName} module...",
            () => appCmd.GenerateModule(projectDir, model));

        // Add all layer projects to .slnx.
        foreach (var rel in NewAppCommand.ModuleProjectPaths(rootNs, moduleName))
        {
            SolutionHelper.AddProject(slnx, rel.Replace('\\', '/'));
        }

        // Wire the module into Program.cs's AddModulus callback (+ using).
        WireModuleRegistration(solutionDir, rootNs, moduleName);

        // Add Host → Infrastructure + Presentation ProjectReferences.
        AddHostProjectReferences(solutionDir, rootNs, moduleNs);

        // Restore so `migrate add` / `dotnet build` works immediately
        // (the newly added csproj isn't in any existing project.assets.json).
        if (!Ux.DryRun)
        {
            Ux.Status("Restoring packages…", () =>
                Ux.RunProcess("dotnet", "restore", solutionDir, "dotnet restore"));
        }

        AnsiConsole.WriteLine();
        Ux.Success($"Module [cyan]{moduleName}[/] added and wired into [grey]{Path.GetFileName(slnx)}[/]");
        if (Ux.DryRun) Ux.Warning("Dry-run: nothing was actually written.");
        AnsiConsole.MarkupLine("[grey]  →[/] Created 4 projects under [grey]{0}[/]", moduleNs);
        AnsiConsole.MarkupLine("[grey]  →[/] Added Host → Infrastructure + Presentation ProjectReferences");
        AnsiConsole.MarkupLine("[grey]  →[/] Registered {0}Module in Program.cs", moduleName);
        AnsiConsole.MarkupLine("[grey]  →[/] Migration engine: {0}", migrationEngine);
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]Next:[/] modulus generate-crud Item --module {0}", moduleName);

        return 0;
    }

    /// <summary>
    /// Wires the new module into the host Program.cs: adds the
    /// <c>using {RootNs}.Modules.{Module}.Infrastructure;</c> directive and a
    /// <c>modules.AddModule&lt;{Module}Module&gt;();</c> line inside the
    /// <c>AddModulus(configuration, modules => { … })</c> callback (appended
    /// last so registration order matches the order modules were added).
    /// </summary>
    private static void WireModuleRegistration(string solutionDir, string rootNs, string moduleName)
    {
        var programCs = Path.Combine(solutionDir, "src", "API", $"{rootNs}.Api", "Program.cs");
        if (!File.Exists(programCs))
            throw new InvalidOperationException(
                $"Program.cs not found at {programCs}. " +
                "Register the module manually with " +
                $"modules.AddModule<{moduleName}Module>() in AddModulus(...).");

        var content = File.ReadAllText(programCs);

        if (content.Contains($"AddModule<{moduleName}Module>()", StringComparison.Ordinal))
            return; // already registered (e.g. re-running add-module)

        if (content.Contains("AddModulus<", StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Program.cs uses the removed AddModulus<TStartupModule> API. " +
                "Modulus modules are now registered explicitly — migrate to " +
                "AddModulus(configuration, modules => { modules.AddModule<...>(); }) " +
                $"and add modules.AddModule<{moduleName}Module>(); there.");

        // 1) Insert the using for the module's Infrastructure namespace (where
        //    the module class lives) at the top of the using block.
        var infraNs = $"{rootNs}.Modules.{moduleName}.Infrastructure";
        var usingLine = $"using {infraNs};";
        if (!content.Contains(usingLine, StringComparison.Ordinal))
        {
            // Insert after the last leading using directive; fall back to the
            // very top when the file has none yet.
            var insertAt = 0;
            for (var i = content.IndexOf("using ", StringComparison.Ordinal);
                 i >= 0;
                 i = content.IndexOf("using ", i + 1, StringComparison.Ordinal))
            {
                var lineEnd = content.IndexOf('\n', i);
                if (lineEnd < 0) break;
                var line = content[i..lineEnd].TrimEnd();
                if (line.StartsWith("using ", StringComparison.Ordinal) && line.EndsWith(';'))
                    insertAt = lineEnd + 1;
                else
                    break;
            }

            content = content.Insert(insertAt, usingLine + "\n");
        }

        // 2) Append modules.AddModule<{Module}Module>(); as the last entry of
        //    the AddModulus callback — inside the closing brace of the
        //    lambda body, before the "});" that closes the call.
        var anchor = content.IndexOf("});", content.IndexOf("AddModulus(", StringComparison.Ordinal), StringComparison.Ordinal);
        if (anchor < 0)
            throw new InvalidOperationException(
                "Could not find the AddModulus(configuration, modules => { … }) " +
                "callback in Program.cs. Register the module manually with " +
                $"modules.AddModule<{moduleName}Module>();");

        // Walk back to the start of the line holding "});" so the new entry
        // is inserted on its own line above it, indented to match siblings.
        var lineStart = content.LastIndexOf('\n', anchor) + 1;
        var indent = "    ";
        var lastReg = content.LastIndexOf("modules.AddModule<", StringComparison.Ordinal);
        if (lastReg >= 0)
        {
            var regLineStart = content.LastIndexOf('\n', lastReg) + 1;
            indent = content[regLineStart..lastReg];
        }

        content = content.Insert(lineStart, $"{indent}modules.AddModule<{moduleName}Module>();\n");

        Ux.WriteFile(programCs, content);
    }

    private static void AddHostProjectReferences(string solutionDir, string rootNs, string moduleNs)
    {
        var apiCsproj = Path.Combine(solutionDir, "src", "API", $"{rootNs}.Api", $"{rootNs}.Api.csproj");
        if (!File.Exists(apiCsproj)) return;

        var content = File.ReadAllText(apiCsproj);

        var refs = new[]
        {
            $"../../Modules/{moduleNs}/{moduleNs}.Infrastructure/{moduleNs}.Infrastructure.csproj",
            $"../../Modules/{moduleNs}/{moduleNs}.Presentation/{moduleNs}.Presentation.csproj",
        };

        var toInsert = "";
        var needBlock = false;
        foreach (var r in refs)
        {
            if (content.Contains(r, StringComparison.Ordinal)) continue;
            toInsert += $"    <ProjectReference Include=\"{r}\" />\n";
            needBlock = true;
        }

        if (!needBlock) return;

        var block = $"  <ItemGroup>\n{toInsert}  </ItemGroup>\n";
        var closeIdx = content.LastIndexOf("</Project>", StringComparison.Ordinal);
        if (closeIdx < 0) return;

        content = content.Insert(closeIdx, block);
        Ux.WriteFile(apiCsproj, content);
    }

    /// <summary>
    /// Derives the application's root namespace by inspecting the existing
    /// project layout under <c>src/</c>.  This is more reliable than using the
    /// .slnx filename: an app scaffolded as <c>modulus app Contoso.Shop</c>
    /// lives in a directory called <c>Shop/</c> but its namespace is
    /// <c>Contoso.Shop</c>, so the .slnx filename is <c>Shop.slnx</c>.
    /// </summary>
    /// <remarks>
    /// Looks for a single <c>*.Api</c> directory under <paramref
    /// name="solutionDir"/>/src/API and strips the <c>.Api</c> suffix.  Returns
    /// null if no host directory is found, in which case the caller falls back
    /// to the .slnx filename.
    /// </remarks>
    private static string? DetectRootNamespace(string solutionDir)
    {
        var apiRoot = Path.Combine(solutionDir, "src", "API");
        if (!Directory.Exists(apiRoot)) return null;

        var apiDirs = Directory.GetDirectories(apiRoot, "*.Api");
        if (apiDirs.Length == 1)
            return Path.GetFileName(apiDirs[0])[..^".Api".Length];

        return null;
    }
}
