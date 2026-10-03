using System.ComponentModel;
using Modulus.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Modulus.Cli.Commands;

/// <summary>
/// Adds a Backend for Frontend (<c>web</c>, <c>mobile</c> or <c>partner</c>) to an existing application:
/// <c>src/Bff/{App}.Bff.{Client}</c>, added to the solution. The auth server, caching and cross-cutting
/// settings are read from the API host so the BFF matches it. Running it again for a client that exists
/// changes nothing.
/// </summary>
internal sealed class AddBffCommand : Command<AddBffCommand.Settings>
{
    internal sealed class Settings : ModulusSettings
    {
        [Description("The client type: web, mobile or partner.")]
        [CommandArgument(0, "<client>")]
        public string Client { get; init; } = "";

        [Description("Auth server: openiddict, keycloak, auth0, okta, azuread, duende, authentik. Omit to detect it from the API host.")]
        [CommandOption("--auth")]
        public string? Auth { get; init; }

        [Description("Microservices: upstream services besides the API host, named after their modules (catalog=http://localhost:5201,orders). Omit to reuse the existing BFFs' services.")]
        [CommandOption("--services")]
        public string? Services { get; init; }
    }

    public override int Execute(CommandContext ctx, Settings s)
    {
        s.Apply();
        return CommandRunner.Run(() => ExecuteCore(s, Environment.CurrentDirectory));
    }

    internal static int ExecuteCore(Settings s, string startDir)
    {
        var clients = BffClients.Parse(s.Client);
        if (clients.Count != 1)
            throw new ArgumentException("Name exactly one client: web, mobile or partner.");
        var client = clients[0];

        var app = ModuleDiscovery.Inventory(startDir)
            ?? throw new InvalidOperationException("No .slnx file found in the current directory tree. Run this command from within a Modulus application.");
        var program = File.Exists(app.ProgramCsPath) ? File.ReadAllText(app.ProgramCsPath) : "";
        var apiCsproj = File.Exists(app.ApiProjectPath) ? File.ReadAllText(app.ApiProjectPath) : "";

        var auth = string.IsNullOrWhiteSpace(s.Auth) ? DetectAuth(program) : s.Auth.Trim().ToLowerInvariant();
        if (auth == "none" || AuthProviders.Find(auth) is null)
        {
            throw new InvalidOperationException(
                "A BFF needs an auth server and none was found in the API host. Pass --auth openiddict|keycloak|auth0|okta|azuread|duende|authentik.");
        }

        if (app.Bffs.FirstOrDefault(b => b.Client == client) is { } existing)
        {
            Ux.Info($"The {client} BFF already exists ({Path.GetRelativePath(app.SolutionDir, existing.ProjectPath).Replace('\\', '/')}); nothing to do.");
            return 0;
        }

        // Upstream services: as given, else the ones the existing BFFs already call (a microservice layout).
        var services = string.IsNullOrWhiteSpace(s.Services)
            ? app.Bffs.SelectMany(b => BffApiClients.ReadServiceAddresses(b.AppSettings))
                .Where(x => x.Name != BffClients.DefaultService && x.Address.Length > 0)
                .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList()
            : BffClients.ParseServices(s.Services);

        // The next free development port after the existing BFFs.
        var ports = app.Bffs.Select(BffApiClients.ReadPort).OfType<int>().ToList();
        var index = ports.Count > 0 ? Math.Max(ports.Max() + 1 - BffClients.FirstPort, app.Bffs.Count) : app.Bffs.Count;

        var example = app.Modules.FirstOrDefault(m => m.Entities.Count > 0);
        var appModel = new AppModel
        {
            RootNamespace = app.RootNamespace,
            AppName = app.RootNamespace.Split('.')[^1],
            Auth = auth,
            Kind = app.Kind ?? AppKind.Api,
            CachingProvider = apiCsproj.Contains("Cobytelabs.Modulus.Caching.Redis", StringComparison.Ordinal) ? "redis" : "inmemory",
            EnableCorrelation = program.Contains("AddModulusCorrelation(", StringComparison.Ordinal),
            EnableSecurityHeaders = program.Contains("AddModulusSecurityHeaders(", StringComparison.Ordinal),
            EnableSecretsGuard = program.Contains("AddModulusSecretsGuard(", StringComparison.Ordinal),
            NoExample = example is null,
            ExampleModule = example?.Name ?? "Catalog",
            ExampleEntity = example?.Entities[0] ?? "Product",
            Bff = [client],
            BffServices = services,
        };

        var model = BffHostModel.For(appModel, client, index);
        // Every module with entities gets its typed client, not only the example one.
        model.Modules = app.Modules.Where(m => m.Entities.Count > 0)
            .Select(m => new BffModuleApiModel
            {
                ProjectName = model.ProjectName,
                ModuleName = m.Name,
                Service = BffModuleApiModel.ServiceFor(m.Name, services.Select(x => x.Name)),
                Entities = m.Entities,
            })
            .ToList();
        var projects = new List<string>();
        new NewAppCommand().GenerateBffHost(app.SolutionDir, model, projects);
        SolutionHelper.AddProject(app.SolutionPath, projects[0]);

        Ux.Success($"Added the [cyan]{client}[/] BFF at [grey]src/Bff/{model.ProjectName}[/] (http://localhost:{model.Port}).");
        AnsiConsole.MarkupLine("[yellow]Next:[/]");
        if (auth == "openiddict")
        {
            AnsiConsole.MarkupLine("  Register its client with the API's token server (API appsettings.json, [grey]Identity[/] section):");
            AnsiConsole.WriteLine($"    \"Seed\": {{ \"Clients\": {{ \"{client}\": {{ \"ClientId\": \"{model.ClientId}\", \"ClientSecret\": \"\" }} }} }}");
            if (client != "web")
                AnsiConsole.WriteLine("    \"EncryptAccessTokens\": false   // the BFF validates JWT access tokens against the JWKS");
            if (client == "partner")
            {
                AnsiConsole.WriteLine("    \"AllowClientCredentialsFlow\": true");
                AnsiConsole.MarkupLine("  and set [grey]Identity:Seed:Clients:partner:ClientSecret[/] with dotnet user-secrets or an environment variable.");
            }
        }
        else
        {
            AnsiConsole.MarkupLine($"  Register client [cyan]{model.ClientId}[/] at your auth server and set [grey]Bff:Authority[/] in the BFF's appsettings.json.");
        }

        return 0;
    }

    /// <summary>The auth provider the API host wires (<c>AddModulusOpenIddict</c>, <c>AddKeycloak</c>, ...), else <c>none</c>.</summary>
    internal static string DetectAuth(string program)
        => AuthProviders.All.FirstOrDefault(p => p.AddMethod is { } method && program.Contains(method + "(", StringComparison.Ordinal))?.Key
           ?? "none";
}
