using System.ComponentModel;
using Modulus.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Modulus.Cli.Commands;

/// <summary>
/// <c>modulus add-deployment --host shop.example.com --email ops@example.com</c>: writes the production container files of the
/// API host: a multi-stage <c>Dockerfile</c> and <c>.dockerignore</c>, a <c>docker-compose.yml</c> (the API behind Traefik with
/// automatic TLS), the Traefik configuration under <c>deploy/traefik</c>, a documented <c>.env.example</c>, and the
/// <c>deploy/certs</c> folder for the token server's PFX files. Existing files are never overwritten, and the ignore rules
/// gain <c>.env</c> and the certificate folder, so running it again changes nothing.
/// </summary>
internal sealed class AddDeploymentCommand : Command<AddDeploymentCommand.Settings>
{
    private readonly TemplateEngine _templates = new();

    internal sealed class Settings : ModulusSettings
    {
        [Description("The public host name the API is served on, for example shop.example.com (the token issuer and passkey relying party).")]
        [CommandOption("--host")]
        public string? Host { get; init; }

        [Description("The address Let's Encrypt sends certificate notices to.")]
        [CommandOption("--email")]
        public string? Email { get; init; }

        [Description("The reverse proxy: traefik (default; issues its own certificates) or nginx (certificates issued once with certbot; see the compose file).")]
        [CommandOption("--proxy")]
        public string? Proxy { get; init; }

        [Description("Add Prometheus (metrics), Jaeger (traces) and Grafana, bound to localhost, and send the app's OpenTelemetry data to them.")]
        [CommandOption("--monitoring")]
        public bool Monitoring { get; init; }
    }

    public override int Execute(CommandContext ctx, Settings s)
    {
        s.Apply();
        return CommandRunner.Run(() => new AddDeploymentCommand().ExecuteCore(s, Environment.CurrentDirectory));
    }

    internal int ExecuteCore(Settings s, string startDir)
    {
        var host = s.Host?.Trim() ?? "";
        // Public names only: letters, digits and hyphens per label, and a top-level domain (no underscores, no IP literals).
        if (!System.Text.RegularExpressions.Regex.IsMatch(host, @"^(?=.{1,253}$)([a-zA-Z0-9]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?\.)+[a-zA-Z]{2,63}$"))
            throw new ArgumentException("--host must be a public DNS host name, for example shop.example.com.");
        var email = s.Email?.Trim() ?? "";
        if (!email.Contains('@', StringComparison.Ordinal) || email.Contains(' ', StringComparison.Ordinal))
            throw new ArgumentException("--email must be an email address, for example ops@example.com.");

        var proxy = (s.Proxy ?? "traefik").Trim().ToLowerInvariant();
        if (proxy is not ("traefik" or "nginx"))
            throw new ArgumentException("--proxy must be traefik or nginx.");

        var app = ModuleDiscovery.Inventory(startDir)
            ?? throw new InvalidOperationException("No .slnx file found in the current directory tree. Run this command from within a Modulus application.");
        if (!File.Exists(app.ApiProjectPath))
            throw new InvalidOperationException($"The API host project was not found at {app.ApiProjectPath}.");

        var written = new List<string>();
        var root = app.SolutionDir;
        // The container build reads the packages of the local feeds from a copy inside the build context.
        var feeds = DockerNuGetFeeds.Prepare(root);
        written.AddRange(feeds);

        var model = new DeploymentModel
        {
            RootNamespace = app.RootNamespace,
            PublicHost = host,
            AcmeEmail = email,
            Proxy = proxy,
            Monitoring = s.Monitoring,
            LocalFeeds = feeds.Count > 0,
        };

        Write("deploy/Dockerfile", model, Path.Combine(root, "Dockerfile"), written, app);
        Write("deploy/dockerignore", model, Path.Combine(root, ".dockerignore"), written, app);
        Write("deploy/docker-compose.yml", model, Path.Combine(root, "docker-compose.yml"), written, app);
        if (proxy == "traefik")
        {
            Write("deploy/traefik.yml", model, Path.Combine(root, "deploy", "traefik", "traefik.yml"), written, app);
            Write("deploy/traefik-dynamic.yml", model, Path.Combine(root, "deploy", "traefik", "dynamic.yml"), written, app);
        }
        else
        {
            Write("deploy/nginx.conf", model, Path.Combine(root, "deploy", "nginx", "nginx.conf"), written, app);
        }
        Write("deploy/env.example", model, Path.Combine(root, ".env.example"), written, app);
        Write("deploy/backup/backup_sh", model, Path.Combine(root, "deploy", "backup", "backup.sh"), written, app);
        Write("deploy/backup/restore_sh", model, Path.Combine(root, "deploy", "backup", "restore.sh"), written, app);
        Write("deploy/README.md", model, Path.Combine(root, "deploy", "README.md"), written, app);
        if (s.Monitoring)
        {
            Write("deploy/prometheus.yml", model, Path.Combine(root, "deploy", "monitoring", "prometheus.yml"), written, app);
            Write("deploy/grafana-datasources.yml", model, Path.Combine(root, "deploy", "monitoring", "grafana", "datasources.yml"), written, app);
            // The API host exports its OpenTelemetry data only once it calls AddModulusOpenTelemetry; the OpenTelemetry__ settings in
            // .env are inert without it.
            if (ProjectFileService.EnsureCsprojPackageReference(app.ApiProjectPath, ObservabilityWiring.PackageId, FrameworkVersion.Current, Ux.DryRun))
                written.Add(Path.GetRelativePath(root, app.ApiProjectPath).Replace('\\', '/') + " (updated)");
            Edit(ApiHostFiles.FeatureServicesFile(app.ProgramCsPath), ObservabilityWiring.EnsureApiServices, written, app);
        }

        var certs = Path.Combine(root, "deploy", "certs");
        if (!Directory.Exists(certs))
        {
            if (!Ux.DryRun)
            {
                Directory.CreateDirectory(certs);
                _templates.RenderToFile("deploy/certs-README.md", model, Path.Combine(certs, "README.md"));
            }
            written.Add("deploy/certs/ (created)");
        }

        Edit(Path.Combine(root, ".gitignore"), t => EnsureIgnored(t, ".env", "deploy/certs/"), written, app);

        AnsiConsole.MarkupLine("[green]✓[/] Deployment files for {0} on {1}", Markup.Escape(app.RootNamespace), Markup.Escape(host));
        foreach (var f in written.Distinct())
            AnsiConsole.MarkupLine("  [green]→[/] [grey]{0}[/]", Markup.Escape(f));
        // Production refuses to create a schema without migrations: an app generated without any fails at its first start.
        if (!Directory.Exists(Path.Combine(root, "src")) || !Directory.EnumerateDirectories(Path.Combine(root, "src"), "Migrations", SearchOption.AllDirectories).Any())
            AnsiConsole.MarkupLine("  [yellow]![/] [grey]No EF Core migrations found. Run modulus migrate add InitialCreate and commit them before the first production start.[/]");
        AnsiConsole.MarkupLine("  [grey]Next: copy .env.example to .env, put the PFX files in deploy/certs, then docker compose up -d --build.[/]");
        return 0;
    }

    private static string EnsureIgnored(string text, params string[] entries)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Select(l => l.Trim()).ToHashSet(StringComparer.Ordinal);
        var missing = entries.Where(e => !lines.Contains(e)).ToList();
        if (missing.Count == 0)
            return text;
        return text.TrimEnd() + "\n\n# Deployment: secrets and token-server keys\n" + string.Join("\n", missing) + "\n";
    }

    private void Write(string template, DeploymentModel model, string path, List<string> written, ModuleDiscovery.AppInventory app)
    {
        var display = Path.GetRelativePath(app.SolutionDir, path).Replace('\\', '/');
        if (File.Exists(path))
        {
            AnsiConsole.MarkupLine("  [yellow]•[/] [grey]{0}[/] [yellow](exists, skipped)[/]", Markup.Escape(display));
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _templates.RenderToFile(template, model, path);
        written.Add(display);
    }

    // Applies a pure text edit; true when the file changed (and was written, unless --dry-run).
    private static bool Edit(string path, Func<string, string> edit, List<string> written, ModuleDiscovery.AppInventory app)
    {
        if (!File.Exists(path))
            return false;
        var before = File.ReadAllText(path);
        var after = edit(before);
        if (after == before)
            return false;
        Ux.WriteFile(path, after);
        written.Add(Path.GetRelativePath(app.SolutionDir, path).Replace('\\', '/') + " (updated)");
        return true;
    }

    internal sealed class DeploymentModel
    {
        public string RootNamespace { get; init; } = "";
        public string PublicHost { get; init; } = "";
        public string AcmeEmail { get; init; } = "";
        public string Proxy { get; init; } = "traefik";
        public bool Monitoring { get; init; }
        /// <summary>The solution has local NuGet feeds; the image restores from their copies (see <see cref="DockerNuGetFeeds"/>).</summary>
        public bool LocalFeeds { get; init; }
    }
}
