using System.Diagnostics;

namespace Modulus.Cli.Services;

/// <summary>
/// Checks whether the <c>modulus</c> CLI tool itself has a newer version
/// available on NuGet. Reports the installed vs. latest version and the
/// command to update.
/// </summary>
internal static class VersionCheckService
{
    private const string CliPackageId = "Cobytelabs.Modulus.Cli";

    /// <summary>
    /// Checks if the installed <c>modulus</c> CLI tool has a newer version
    /// available on NuGet.
    /// </summary>
    public static async Task<CliVersionCheckResult> CheckCliUpdatesAsync(
        CancellationToken ct = default)
    {
        var installedVersion = GetInstalledCliVersion();
        if (installedVersion is null)
        {
            return new CliVersionCheckResult(
                HasUpdate: false,
                InstalledVersion: "unknown",
                LatestVersion: "unknown",
                UpdateCommand: null,
                Error: "Could not determine installed CLI version.");
        }

        var latestVersion = await NuGetVersionService.GetLatestVersionAsync(CliPackageId, ct);
        if (latestVersion is null)
        {
            return new CliVersionCheckResult(
                HasUpdate: false,
                InstalledVersion: installedVersion,
                LatestVersion: "unknown",
                UpdateCommand: null,
                Error: "Could not query NuGet for latest CLI version.");
        }

        var hasUpdate = NuGetVersionService.IsNewer(latestVersion, installedVersion);
        var updateCommand = hasUpdate
            ? $"dotnet tool update -g {CliPackageId}"
            : null;

        return new CliVersionCheckResult(
            HasUpdate: hasUpdate,
            InstalledVersion: installedVersion,
            LatestVersion: latestVersion,
            UpdateCommand: updateCommand,
            Error: null);
    }

    /// <summary>
    /// Gets the installed version of the <c>modulus</c> CLI tool by running
    /// <c>dotnet tool list -g</c> and parsing the output.
    /// </summary>
    public static string? GetInstalledCliVersion()
    {
        try
        {
            var psi = new ProcessStartInfo("dotnet", "tool list -g")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using var proc = Process.Start(psi);
            if (proc is null) return null;

            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(5000);

            if (proc.ExitCode != 0) return null;

            // Parse output lines looking for "Cobytelabs.Modulus.Cli" package.
            foreach (var line in output.Split('\n'))
            {
                if (line.Contains(CliPackageId, StringComparison.OrdinalIgnoreCase))
                {
                    // Typical format: "Cobytelabs.Modulus.Cli     1.2.0      ..."
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    // The version is typically the second column.
                    if (parts.Length >= 2)
                        return parts[1].Trim();
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>Result of checking the CLI tool for updates.</summary>
internal sealed record CliVersionCheckResult(
    bool HasUpdate,
    string InstalledVersion,
    string LatestVersion,
    string? UpdateCommand,
    string? Error);
