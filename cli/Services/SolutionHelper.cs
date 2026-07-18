using System.Diagnostics;
using System.Xml.Linq;

namespace Modulus.Cli.Services;

/// <summary>
/// Creates and modifies .slnx solution files, organising projects into solution
/// folders that mirror the on-disk module/layer structure.
/// </summary>
/// <remarks>
/// The .slnx format requires <b>all</b> <c>&lt;Folder&gt;</c> elements to be
/// direct children of <c>&lt;Solution&gt;</c> (siblings), with
/// <c>&lt;Project&gt;</c> elements one level deep.  Nested folders are not
/// traversed by the .NET SDK for project/test discovery, so we collapse each
    /// module's four layer projects into a single top-level folder named after the
/// module (e.g. <c>/src/App.Modules.Catalog/</c>).
/// </remarks>
internal static class SolutionHelper
{
    /// <summary>
    /// Creates a new .slnx with the given projects grouped into top-level
    /// solution folders.  Each project is placed under a folder named after its
    /// module (or kernel) directory so the IDE shows a navigable tree:
    /// <code>
    /// /src/App.Host/          (host project)
    /// /src/App.Shared/        (4 kernel projects)
    /// /src/Modules/App.Modules.Catalog/  (4 layer projects)
    /// /tests/App.Tests/       (top-level tests)
    /// </code>
    /// </summary>
    public static void Create(string slnxPath, string solutionName, IEnumerable<string> projectPaths)
    {
        var slnx = new XDocument(new XElement("Solution"));
        foreach (var path in projectPaths)
            AddProjectElement(slnx.Root!, path.Replace('\\', '/'));

        File.WriteAllText(slnxPath, slnx.ToString() + "\n");
    }

    /// <summary>
    /// Adds a project to an existing .slnx file, placing it under the solution
    /// folder that matches its module/kernel directory (creating the folder as a
    /// top-level sibling if needed).  Idempotent.
    /// </summary>
    public static void AddProject(string slnxPath, string projectRelativePath)
    {
        var normalized = projectRelativePath.Replace('\\', '/');
        var content = File.ReadAllText(slnxPath);
        var doc = XDocument.Parse(content);

        var alreadyPresent = doc.Root!.Elements("Folder")
            .Elements("Project")
            .Concat(doc.Root!.Elements("Project"))
            .Any(p => string.Equals(
                p.Attribute("Path")?.Value,
                normalized,
                StringComparison.OrdinalIgnoreCase));
        if (alreadyPresent) return;

        AddProjectElement(doc.Root!, normalized);
        File.WriteAllText(slnxPath, doc.ToString() + "\n");
    }

    /// <summary>
    /// Computes the solution-folder key for a project and inserts a
    /// <c>&lt;Project/&gt;</c> element under that top-level folder (creating the
    /// folder if needed).  A module's four layer projects collapse under a
    /// single module folder because they share the same grandparent directory.
    /// </summary>
    private static void AddProjectElement(XElement root, string projectPath)
    {
        var parts = projectPath.Split('/');
        // 4+ segments (src/Module/ProjectDir/file.csproj) → drop last 2 → module folder.
        // 3 segments  (src/Host/file.csproj)              → drop last 1 → host folder.
        var keySegments = parts.Length switch
        {
            >= 4 => parts[..^2],
            3 => parts[..^1],
            _ => parts[..^1],
        };
        var folderKey = string.Join('/', keySegments);

        var folder = EnsureTopLevelFolder(root, folderKey);
        folder.Add(new XElement("Project", new XAttribute("Path", projectPath)));
    }

    /// <summary>
    /// Finds or creates a <b>top-level</b> <c>&lt;Folder&gt;</c> (direct child
    /// of <c>&lt;Solution&gt;</c>) with the given key, e.g.
    /// <c>"src/App.Modules.Catalog"</c> → <c>&lt;Folder Name="/src/App.Modules.Catalog/"/&gt;</c>.
    /// </summary>
    private static XElement EnsureTopLevelFolder(XElement root, string folderKey)
    {
        var virtualName = "/" + folderKey.Trim('/') + "/";
        var folder = root.Elements("Folder").FirstOrDefault(f =>
            string.Equals(f.Attribute("Name")?.Value, virtualName, StringComparison.OrdinalIgnoreCase));

        if (folder is null)
        {
            folder = new XElement("Folder", new XAttribute("Name", virtualName));
            root.Add(folder);
        }

        return folder;
    }

    /// <summary>
    /// Finds the .slnx file in the current or parent directories.
    /// </summary>
    public static string? FindSolution(string startDir)
    {
        var dir = startDir;
        while (dir is not null)
        {
            var slnx = Directory.GetFiles(dir, "*.slnx");
            if (slnx.Length > 0) return slnx[0];
            dir = Directory.GetParent(dir)?.FullName;
        }
        return null;
    }

    /// <summary>
    /// Runs a dotnet CLI command and streams output to the console.
    /// </summary>
    public static int RunDotNet(string args, string? workingDir = null)
    {
        var psi = new ProcessStartInfo("dotnet", args)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = workingDir ?? Environment.CurrentDirectory,
        };

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start dotnet process.");
        proc.WaitForExit();
        return proc.ExitCode;
    }
}
