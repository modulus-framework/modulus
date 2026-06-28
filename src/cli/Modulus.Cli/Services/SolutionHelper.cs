using System.Diagnostics;
using System.Xml.Linq;

namespace Modulus.Cli.Services;

/// <summary>
/// Creates and modifies .slnx solution files.
/// </summary>
internal static class SolutionHelper
{
    /// <summary>
    /// Creates a new .slnx with the given projects.
    /// </summary>
    public static void Create(string slnxPath, string solutionName, IEnumerable<string> projectPaths)
    {
        var slnx = new XDocument(
            new XElement("Solution",
                new XElement("Folder", new XAttribute("Name", "/src/")),
                new XElement("Folder", new XAttribute("Name", "/tests/"))));

        foreach (var path in projectPaths)
        {
            slnx.Root!.Add(new XElement("Project",
                new XAttribute("Path", path.Replace('\\', '/'))));
        }

        File.WriteAllText(slnxPath, slnx.ToString() + "\n");
    }

    /// <summary>
    /// Adds a project to an existing .slnx file.
    /// </summary>
    public static void AddProject(string slnxPath, string projectRelativePath)
    {
        var content = File.ReadAllText(slnxPath);
        var doc = XDocument.Parse(content);

        // Don't add if already present
        var existing = doc.Root!.Elements("Project")
            .Any(p => string.Equals(
                p.Attribute("Path")?.Value,
                projectRelativePath.Replace('\\', '/'),
                StringComparison.OrdinalIgnoreCase));
        if (existing) return;

        doc.Root!.Add(new XElement("Project",
            new XAttribute("Path", projectRelativePath.Replace('\\', '/'))));

        File.WriteAllText(slnxPath, doc.ToString() + "\n");
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
