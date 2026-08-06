using System.ComponentModel;
using Modulus.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Modulus.Cli.Commands;

/// <summary>
/// Adds a layered module to an existing application and wires it into
/// the host module's [DependsOn] and the Host project's ProjectReferences.
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
            : s.Database!;

        var model = new ModuleModel
        {
            RootNamespace = rootNs,
            ModuleName = moduleName,
            ModuleNamespace = moduleNs,
            DbProvider = database,
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

        // Wire [DependsOn] + using into the host module class.
        WireDependsOn(solutionDir, rootNs, moduleName);

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
        AnsiConsole.MarkupLine("[grey]  →[/] Added [[DependsOn]] to host module");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]Next:[/] modulus generate-crud Item --module {0}", moduleName);

        return 0;
    }

    private static void WireDependsOn(string solutionDir, string rootNs, string moduleName)
    {
        var apiDir = Path.Combine(solutionDir, "src", "API", $"{rootNs}.Api");
        var moduleFiles = Directory.GetFiles(apiDir, "*Module.cs", SearchOption.AllDirectories);

        var moduleClass = $"{moduleName}Module";
        var infraNs = $"{rootNs}.Modules.{moduleName}.Infrastructure";

        foreach (var file in moduleFiles)
        {
            var content = File.ReadAllText(file);
            if (content.Contains($"typeof({moduleClass})", StringComparison.Ordinal))
                continue;

            // Add the using for the module's Infrastructure namespace (where the
            // module class lives) unless already present.
            var usingLine = $"using {infraNs};";
            if (!content.Contains(usingLine, StringComparison.Ordinal))
            {
                // Insert before the namespace declaration to keep usings at top.
                // Use >= 0 (not > 0) so a file that starts with "namespace" at
                // position 0 is handled correctly.
                var nsIdx = content.IndexOf("namespace ", StringComparison.Ordinal);
                if (nsIdx >= 0)
                    content = content.Insert(nsIdx, usingLine + "\n");
            }

            // Add [DependsOn(typeof({Module}Module))] before the class declaration.
            // Handle two cases:
            //  a) An existing [DependsOn(...)] attribute is present — insert a new
            //     one before it so we don't nest inside an existing attribute list.
            //  b) No [DependsOn] exists — insert before the class declaration.
            var attribute = $"[DependsOn(typeof({moduleClass}))]\n";

            var existingDepAttr = content.IndexOf("[DependsOn(", StringComparison.Ordinal);
            var classPattern = "public sealed class";
            var classIdx = content.IndexOf(classPattern, StringComparison.Ordinal);
            if (classIdx < 0) continue;

            int insertIdx;
            if (existingDepAttr >= 0 && existingDepAttr < classIdx)
            {
                // Insert before the first [DependsOn] block.
                insertIdx = content.LastIndexOf('\n', existingDepAttr) + 1;
            }
            else
            {
                // No existing [DependsOn] — insert on the line before the class.
                insertIdx = content.LastIndexOf('\n', classIdx) + 1;
            }

            content = content.Insert(insertIdx, attribute);
            Ux.WriteFile(file, content);
        }
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
