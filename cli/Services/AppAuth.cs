namespace Modulus.Cli.Services;

/// <summary>
/// Whether the application a generated file belongs to signs users in. The host's pipeline calls
/// <c>app.UseAuthentication()</c> only when an auth provider is configured, so that line is the signal. UI templates use it
/// to add <c>[Authorize]</c> only where an authentication scheme exists; without one, an authorized page answers 500.
/// </summary>
internal static class AppAuth
{
    public static bool Enabled(string filePath)
    {
        var dir = Path.GetFullPath(Path.GetDirectoryName(Path.GetFullPath(filePath))!);
        // The file may be in a folder the app has not created yet; search from the nearest existing folder up.
        while (!Directory.Exists(dir) && Path.GetDirectoryName(dir) is { } parent)
            dir = parent;

        // The pipeline lives in the host's setup files (Security/Middleware), so the whole host is read, not Program.cs alone.
        var program = ModuleDiscovery.Inventory(dir)?.UiProgramCsPath;
        return program is not null && File.Exists(program)
            && ApiHostFiles.ReadAll(program).Contains("app.UseAuthentication()", StringComparison.Ordinal);
    }
}
