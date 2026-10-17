namespace Modulus.Cli.Services;

/// <summary>
/// The API host's composition, spread over setup files: Program.cs only composes them, so the modules are registered in
/// Composition/ModuleRegistration.cs, the optional features' services in Configuration/FeatureServicesSetup.cs and their
/// endpoints in Endpoints/FeatureEndpoints.cs. Readers look at the whole host; writers go to the file that owns the concern.
/// A host generated before the split keeps everything in Program.cs, and every target falls back to it.
/// </summary>
internal static class ApiHostFiles
{
    private const string FeatureServicesMarker = "AddApiFeatures(";
    private const string FeatureEndpointsMarker = "MapApiFeatures(";
    private const string ModulesMarker = "AddModulus(";

    /// <summary>
    /// The host's sources: Program.cs first, then the .cs files of its folder and of the setup folders one level below it
    /// (Composition, Configuration, Security, ...). The host lays its setup files out that way, so the scan stays in the host.
    /// </summary>
    public static IReadOnlyList<string> Sources(string programPath)
    {
        if (!File.Exists(programPath))
            return [];

        var dir = Path.GetDirectoryName(programPath)!;
        var options = new EnumerationOptions { IgnoreInaccessible = true };
        var folders = Directory.EnumerateDirectories(dir, "*", options)
            .Where(d => Path.GetFileName(d) is not ("bin" or "obj"))
            .Prepend(dir);
        var others = folders
            .SelectMany(folder => Directory.EnumerateFiles(folder, "*.cs", options))
            .Where(f => !string.Equals(Path.GetFullPath(f), Path.GetFullPath(programPath), StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.Ordinal);
        return [programPath, .. others];
    }

    /// <summary>The whole host's text: every source file, so a detection sees a feature wherever it is composed.</summary>
    public static string ReadAll(string programPath)
        => string.Concat(Sources(programPath).Select(File.ReadAllText));

    /// <summary>The file that composes the module registration (<c>AddModulus(...)</c>).</summary>
    public static string ModulesFile(string programPath)
        => Owner(programPath, ["Composition", "ModuleRegistration.cs"], ModulesMarker);

    /// <summary>The first host file containing <paramref name="marker"/>, else Program.cs.</summary>
    public static string FileWith(string programPath, string marker)
        => Sources(programPath).FirstOrDefault(f => File.ReadAllText(f).Contains(marker, StringComparison.Ordinal)) ?? programPath;

    /// <summary>The file the optional features' services go to (<c>Configuration/FeatureServicesSetup.cs</c>).</summary>
    public static string FeatureServicesFile(string programPath)
        => Owner(programPath, ["Configuration", "FeatureServicesSetup.cs"], FeatureServicesMarker);

    /// <summary>The file the optional features' endpoints go to (<c>Endpoints/FeatureEndpoints.cs</c>).</summary>
    public static string FeatureEndpointsFile(string programPath)
        => Owner(programPath, ["Endpoints", "FeatureEndpoints.cs"], FeatureEndpointsMarker);

    /// <summary>
    /// Inserts <paramref name="block"/> where the optional features' services belong: before the feature method's
    /// <c>return builder;</c>, or before <c>var app = builder.Build();</c> in a host generated before the split.
    /// </summary>
    public static string InsertFeatureServices(string text, string block, string newLine)
        => text.Contains(FeatureServicesMarker, StringComparison.Ordinal)
            ? InsertBefore(text, "        return builder;", block, newLine)
            : InsertBefore(text, "var app = builder.Build();", block, newLine);

    /// <summary>
    /// Inserts <paramref name="block"/> where the optional features' endpoints belong: before the feature method's
    /// <c>return app;</c>, or after the module endpoints in a host generated before the split (<c>null</c> when neither exists).
    /// </summary>
    public static string? InsertFeatureEndpoints(string text, string block, string newLine)
    {
        if (text.Contains(FeatureEndpointsMarker, StringComparison.Ordinal))
            return InsertBefore(text, "        return app;", block, newLine);

        var endpoints = text.IndexOf("app.MapModulusEndpoints(", StringComparison.Ordinal);
        var end = endpoints < 0 ? -1 : text.IndexOf(".ToArray());", endpoints, StringComparison.Ordinal);
        var lineEnd = end < 0 ? -1 : text.IndexOf('\n', end);
        if (lineEnd >= 0)
            return text[..(lineEnd + 1)] + newLine + block + text[(lineEnd + 1)..];

        var run = text.IndexOf("app.Run();", StringComparison.Ordinal);
        return run < 0 ? null : text[..run] + block + newLine + text[run..];
    }

    /// <summary>Inserts <paramref name="block"/> on the line before the first <paramref name="anchor"/>; the text unchanged when there is none.</summary>
    private static string InsertBefore(string text, string anchor, string block, string newLine)
    {
        var at = text.IndexOf(anchor, StringComparison.Ordinal);
        if (at < 0)
            return text;
        var lineStart = text.LastIndexOf('\n', at) + 1;
        return text[..lineStart] + block + newLine + text[lineStart..];
    }

    /// <summary>The setup file when it exists and composes <paramref name="marker"/>, else Program.cs.</summary>
    private static string Owner(string programPath, string[] relative, string marker)
    {
        var dir = Path.GetDirectoryName(programPath)!;
        var path = Path.Combine([dir, .. relative]);
        return File.Exists(path) && File.ReadAllText(path).Contains(marker, StringComparison.Ordinal) ? path : programPath;
    }
}
