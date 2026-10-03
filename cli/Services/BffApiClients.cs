using System.Text.Json;
using System.Text.RegularExpressions;

namespace Modulus.Cli.Services;

/// <summary>
/// The BFF projects of an app (<c>src/Bff/*</c>, marked <c>&lt;ModulusAppKind&gt;bff-{client}&lt;/ModulusAppKind&gt;</c>) and
/// their per-module typed clients (<c>ApiClients/{Module}Api.cs</c>, registered in <c>ApiClients/ApiClientRegistration.cs</c>).
/// <c>modulus app --bff</c>, <c>add-bff</c>, <c>generate-crud</c> and <c>generate-bff-endpoint</c> keep them complete:
/// every module with entities has a client in every BFF, with the read methods of each entity.
/// </summary>
internal static partial class BffApiClients
{
    /// <summary>One BFF project.</summary>
    internal sealed record BffProject(string Client, string ProjectPath)
    {
        public string Directory => Path.GetDirectoryName(ProjectPath)!;

        public string ProjectName => Path.GetFileNameWithoutExtension(ProjectPath);

        public string ProgramCs => Path.Combine(Directory, "Program.cs");

        public string AppSettings => Path.Combine(Directory, "appsettings.json");
    }

    /// <summary>The BFF projects under <c>src/Bff</c>, in client menu order.</summary>
    public static IReadOnlyList<BffProject> Discover(string solutionDir)
    {
        var root = Path.Combine(solutionDir, "src", "Bff");
        if (!System.IO.Directory.Exists(root))
            return [];

        var projects = new List<BffProject>();
        foreach (var csproj in System.IO.Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories))
        {
            if (KindMarker().Match(File.ReadAllText(csproj)) is { Success: true } match)
                projects.Add(new BffProject(match.Groups[1].Value.ToLowerInvariant(), csproj));
        }

        return projects.OrderBy(p => Array.IndexOf(BffClients.Kinds, p.Client)).ThenBy(p => p.ProjectName, StringComparer.Ordinal).ToList();
    }

    /// <summary>The upstream service names in a BFF's <c>appsettings.json</c> (<c>Bff:Services</c>).</summary>
    public static IReadOnlyList<string> ReadServices(string appSettingsPath)
        => ReadServiceAddresses(appSettingsPath).Select(s => s.Name).ToList();

    /// <summary>The upstream services (name, address) in a BFF's <c>appsettings.json</c>, <c>api</c> included.</summary>
    public static IReadOnlyList<BffServiceModel> ReadServiceAddresses(string appSettingsPath)
    {
        if (!File.Exists(appSettingsPath))
            return [];
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(appSettingsPath),
                new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (!doc.RootElement.TryGetProperty("Bff", out var bff) || !bff.TryGetProperty("Services", out var services)
                || services.ValueKind != JsonValueKind.Object)
            {
                return [];
            }

            return services.EnumerateObject()
                .Select(p => new BffServiceModel
                {
                    Name = p.Name,
                    Address = p.Value.ValueKind == JsonValueKind.Object && p.Value.TryGetProperty("Address", out var a) ? a.GetString() ?? "" : "",
                })
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>The development port of a BFF (its <c>launchSettings.json</c> <c>applicationUrl</c>), or null.</summary>
    public static int? ReadPort(BffProject bff)
    {
        var launch = Path.Combine(bff.Directory, "Properties", "launchSettings.json");
        if (!File.Exists(launch))
            return null;
        var match = PortPattern().Match(File.ReadAllText(launch));
        return match.Success ? int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : null;
    }

    /// <summary>The full <c>ApiClients/{Module}Api.cs</c> of <paramref name="module"/>.</summary>
    public static string RenderModule(TemplateEngine templates, BffModuleApiModel module)
    {
        module.EntityMethods = string.Join("\n", module.Entities.Select(e => templates.Render("bff/ModuleApiEntity", module.EntityModel(e))));
        return templates.Render("bff/ModuleApi", module);
    }

    /// <summary>
    /// Makes sure <paramref name="bff"/> has <paramref name="module"/>'s typed client with the methods of every entity in
    /// <paramref name="entities"/>, registered, and that <c>Program.cs</c> calls <c>AddModuleApiClients()</c>. Existing methods
    /// and registrations are left alone. Returns the files written (relative to the BFF directory).
    /// </summary>
    public static IReadOnlyList<string> EnsureModule(TemplateEngine templates, BffProject bff, string module, IReadOnlyList<string> entities)
    {
        var changed = new List<string>();
        var model = new BffModuleApiModel
        {
            ProjectName = bff.ProjectName,
            ModuleName = module,
            Service = BffModuleApiModel.ServiceFor(module, ReadServices(bff.AppSettings)),
            Entities = entities,
        };

        var clientFile = Path.Combine(bff.Directory, "ApiClients", $"{module}Api.cs");
        if (!File.Exists(clientFile))
        {
            Ux.WriteFile(clientFile, RenderModule(templates, model));
            changed.Add($"ApiClients/{module}Api.cs");
        }
        else
        {
            var text = File.ReadAllText(clientFile);
            var nl = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            var updated = text;
            foreach (var entity in entities)
            {
                if (updated.Contains($" Get{CodeGen.Pluralize(entity)}Async(", StringComparison.Ordinal))
                    continue;
                var close = updated.LastIndexOf('}');
                if (close < 0)
                    break;
                var block = templates.Render("bff/ModuleApiEntity", model.EntityModel(entity)).Replace("\n", nl, StringComparison.Ordinal);
                var before = updated[..close].TrimEnd();
                updated = before + nl + nl + block + updated[close..];
            }

            if (updated != text)
            {
                Ux.WriteFile(clientFile, updated);
                changed.Add($"ApiClients/{module}Api.cs (updated)");
            }
        }

        var registration = Path.Combine(bff.Directory, "ApiClients", "ApiClientRegistration.cs");
        if (!File.Exists(registration))
        {
            Ux.WriteFile(registration, templates.Render("bff/ApiClients", new { bff.ProjectName, Modules = new[] { model } }));
            changed.Add("ApiClients/ApiClientRegistration.cs");
        }
        else
        {
            var text = File.ReadAllText(registration);
            const string anchor = "        return services;";
            var index = text.IndexOf(anchor, StringComparison.Ordinal);
            if (!text.Contains($"<{module}Api>", StringComparison.Ordinal) && index >= 0)
            {
                var nl = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
                Ux.WriteFile(registration, text[..index] + $"        services.AddBffApiClient<{module}Api>(\"{model.Service}\");" + nl + text[index..]);
                changed.Add("ApiClients/ApiClientRegistration.cs (updated)");
            }
        }

        if (EnsureProgramWiring(bff))
            changed.Add("Program.cs (updated)");
        return changed;
    }

    /// <summary>Adds <c>using {Project}.ApiClients;</c> and <c>builder.Services.AddModuleApiClients();</c> when missing.</summary>
    private static bool EnsureProgramWiring(BffProject bff)
    {
        if (!File.Exists(bff.ProgramCs))
            return false;
        var text = File.ReadAllText(bff.ProgramCs);
        var original = text;
        var nl = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

        if (!text.Contains("AddModuleApiClients(", StringComparison.Ordinal))
        {
            var anchor = text.IndexOf("var app = builder.Build();", StringComparison.Ordinal);
            if (anchor < 0)
                return false;
            text = text[..anchor] + "builder.Services.AddModuleApiClients();" + nl + nl + text[anchor..];
        }

        var usingLine = $"using {bff.ProjectName}.ApiClients;";
        if (!text.Contains(usingLine, StringComparison.Ordinal))
            text = usingLine + nl + text;

        if (text == original)
            return false;
        Ux.WriteFile(bff.ProgramCs, text);
        return true;
    }

    /// <summary>Wires an endpoint class's <c>Map{Name}Endpoints()</c> into <c>Program.cs</c> after <c>MapModulusBff()</c>.</summary>
    public static bool EnsureEndpointMapped(BffProject bff, string mapMethod)
    {
        if (!File.Exists(bff.ProgramCs))
            return false;
        var text = File.ReadAllText(bff.ProgramCs);
        var original = text;
        var nl = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

        if (!text.Contains($"app.{mapMethod}(", StringComparison.Ordinal))
        {
            const string anchor = "app.MapModulusBff();";
            var index = text.IndexOf(anchor, StringComparison.Ordinal);
            if (index < 0)
                return false;
            var end = index + anchor.Length;
            text = text[..end] + nl + $"app.{mapMethod}();" + text[end..];
        }

        var usingLine = $"using {bff.ProjectName}.Endpoints;";
        if (!text.Contains(usingLine, StringComparison.Ordinal))
            text = usingLine + nl + text;

        if (text == original)
            return false;
        Ux.WriteFile(bff.ProgramCs, text);
        return true;
    }

    [GeneratedRegex(@"""applicationUrl""\s*:\s*""https?://[^:""]+:(\d+)")]
    private static partial Regex PortPattern();

    [GeneratedRegex(@"<ModulusAppKind>\s*bff-(\w+)\s*</ModulusAppKind>", RegexOptions.IgnoreCase)]
    private static partial Regex KindMarker();
}
