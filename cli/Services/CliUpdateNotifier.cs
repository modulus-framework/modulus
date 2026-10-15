using System.Reflection;
using System.Text.Json;

namespace Modulus.Cli.Services;

/// <summary>
/// Tells the developer when a newer <c>modulus</c> CLI is on NuGet. The check runs in the background while the command
/// runs, asks nuget.org at most once a day (the answer is cached in <c>~/.modulus/update-check.json</c>), and never
/// delays or fails a command. The notice is skipped for <c>--json</c>, <c>--quiet</c>, redirected output and CI, and it
/// can be turned off with <c>MODULUS_NO_UPDATE_CHECK=1</c>.
/// </summary>
internal static class CliUpdateNotifier
{
    private const string CliPackageId = "Synvro.Modulus.Cli";
    private const string DisableVariable = "MODULUS_NO_UPDATE_CHECK";
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(24);
    private static readonly TimeSpan CheckBudget = TimeSpan.FromSeconds(1);

    private static string CachePath => Path.Combine(UiTemplatePackage.UserRoot, "update-check.json");

    /// <summary>Starts the check, or returns a completed task with no result when it is switched off for this run.</summary>
    public static Task<string?> Start(string[] args)
    {
        if (IsDisabled(args)) return Task.FromResult<string?>(null);
        return Task.Run(LatestVersionAsync);
    }

    /// <summary>Prints the notice when the check has finished, found a newer version, and the output is for a person.</summary>
    public static void Finish(Task<string?> check, string[] args)
    {
        if (IsDisabled(args)) return;
        try
        {
            // Wait briefly for the check; a slow network must not hold up the command's exit.
            if (!check.Wait(CheckBudget)) return;
            var notice = BuildNotice(check.Result, InstalledVersion());
            if (notice is null) return;

            Spectre.Console.AnsiConsole.WriteLine();
            Spectre.Console.AnsiConsole.MarkupLine(notice);
        }
        catch
        {
            // The notice is best effort.
        }
    }

    /// <summary>The notice to print (Spectre markup, two lines), or null when the installed version is current or unknown.</summary>
    internal static string? BuildNotice(string? latest, string? installed)
    {
        if (latest is null || installed is null || !NuGetVersionService.IsNewer(latest, installed)) return null;
        return
            $"[yellow]A new version of the Modulus CLI is available:[/] [green]{latest}[/] [grey](you have {installed})[/]\n" +
            $"[grey]Update with:[/] dotnet tool update -g {CliPackageId}";
    }

    private static bool IsDisabled(string[] args)
    {
        if (Environment.GetEnvironmentVariable(DisableVariable) is { Length: > 0 } flag && flag != "0") return true;
        if (Environment.GetEnvironmentVariable("CI") is { Length: > 0 } ci && ci != "false" && ci != "0") return true;
        if (Console.IsOutputRedirected) return true;
        if (Ux.Json || Ux.Quiet) return true;
        return args.Any(a => a is "--json" or "-q" or "--quiet");
    }

    /// <summary>The version of the running CLI, from its informational version (for example <c>1.5.2</c>).</summary>
    internal static string? InstalledVersion()
    {
        var info = typeof(CliUpdateNotifier).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(info)) return null;
        // Drop build metadata such as "+<commit>" that the SDK appends.
        var plus = info.IndexOf('+');
        return plus >= 0 ? info[..plus] : info;
    }

    /// <summary>The latest stable version: from the cache when it is fresh, otherwise from nuget.org (and the cache is refreshed).</summary>
    internal static async Task<string?> LatestVersionAsync()
    {
        var cached = ReadCache();
        if (cached is { } c && DateTimeOffset.UtcNow - c.CheckedAt < CacheLifetime)
            return c.Latest;

        var latest = await NuGetVersionService.GetLatestVersionAsync(CliPackageId);
        if (latest is not null)
            WriteCache(new Cache(DateTimeOffset.UtcNow, latest));
        return latest ?? cached?.Latest;
    }

    private sealed record Cache(DateTimeOffset CheckedAt, string Latest);

    private static Cache? ReadCache()
    {
        try
        {
            return File.Exists(CachePath)
                ? JsonSerializer.Deserialize<Cache>(File.ReadAllText(CachePath))
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static void WriteCache(Cache cache)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
            File.WriteAllText(CachePath, JsonSerializer.Serialize(cache));
        }
        catch
        {
            // A read-only home folder only means the check runs again next time.
        }
    }
}
