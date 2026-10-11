namespace Modulus.Cli.Services;

/// <summary>
/// The BFF clients <c>modulus app --bff</c> / <c>modulus add-bff</c> can generate: one deployable backend per
/// client type, each under <c>src/Bff/{App}.Bff.{Client}</c>.
/// </summary>
internal static class BffClients
{
    /// <summary>Valid client types, in menu order.</summary>
    public static readonly string[] Kinds = ["web", "mobile", "partner"];

    /// <summary>The first BFF's port; each further client takes the next one (the API is 5180, the Web host 5181).</summary>
    public const int FirstPort = 5190;

    /// <summary>The API host's development address, the <c>api</c> upstream every generated BFF starts with.</summary>
    public const string ApiBaseUrl = "http://localhost:5180";

    /// <summary>Parses <c>web,mobile</c> (case-insensitive, de-duplicated, in menu order); empty or <c>none</c> = no BFF.</summary>
    public static IReadOnlyList<string> Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Equals("none", StringComparison.OrdinalIgnoreCase))
            return [];

        var requested = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(v => v.ToLowerInvariant())
            .ToList();
        var unknown = requested.Where(v => !Kinds.Contains(v)).ToList();
        if (unknown.Count > 0)
            throw new ArgumentException($"Unknown BFF client '{unknown[0]}'. Valid: {string.Join(", ", Kinds)}.");
        return Kinds.Where(requested.Contains).ToList();
    }

    /// <summary>
    /// Parses <c>--services catalog=http://localhost:5201,orders</c>: the upstream services of a microservice layout besides
    /// <c>api</c>, each named after the module it serves (its routes are <c>/api/{name}/...</c>). A name without an address
    /// is resolved through service discovery (<c>https+http://{name}</c>).
    /// </summary>
    public static IReadOnlyList<BffServiceModel> ParseServices(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return [];

        var services = new List<BffServiceModel>();
        foreach (var entry in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = entry.Split('=', 2, StringSplitOptions.TrimEntries);
            var name = parts[0].ToLowerInvariant();
            if (!System.Text.RegularExpressions.Regex.IsMatch(name, "^[a-z][a-z0-9-]*$"))
                throw new ArgumentException($"Invalid service name '{parts[0]}': use the module's name, e.g. catalog=http://localhost:5201.");
            if (name == DefaultService)
                throw new ArgumentException("'api' is the API host itself (Api:BaseUrl); name the other services after their modules.");
            if (services.Exists(x => x.Name == name))
                throw new ArgumentException($"Service '{name}' is listed twice.");

            var address = parts.Length == 2 && parts[1].Length > 0 ? parts[1] : $"https+http://{name}";
            if (!Uri.TryCreate(address, UriKind.Absolute, out _))
                throw new ArgumentException($"Service '{name}' has an invalid address '{address}'.");
            services.Add(new BffServiceModel { Name = name, Address = address });
        }

        return services;
    }

    /// <summary>The upstream every module client uses unless the module has its own service.</summary>
    public const string DefaultService = "api";

    /// <summary>The <c>Bff:AuthServer</c> value for a <c>--auth</c> key; every supported server works through OIDC discovery.</summary>
    public static string AuthServer(string auth) => auth switch
    {
        "openiddict" => "OpenIddict",
        "keycloak" => "Keycloak",
        "auth0" => "Auth0",
        "okta" => "Okta",
        "azuread" => "AzureAd",
        "duende" => "Duende",
        "authentik" => "Authentik",
        _ => "Generic",
    };

    /// <summary>
    /// The token server the BFFs validate against: the API host for the local OpenIddict server, else the provider's
    /// issuer in the shape that provider uses (placeholders the developer fills in, like the API's own settings).
    /// </summary>
    public static string Authority(string auth, string appNameLower) => auth switch
    {
        "openiddict" => ApiBaseUrl,
        "keycloak" => "https://localhost:8443/realms/master",
        "auth0" => "https://your-tenant.auth0.com/",
        "okta" => "https://your-tenant.okta.com/oauth2/default",
        "azuread" => "https://login.microsoftonline.com/your-tenant-id/v2.0",
        "duende" => "https://localhost:5001",
        "authentik" => $"https://authentik.example.com/application/o/{appNameLower}/",
        _ => "",
    };
}

/// <summary>One BFF client of the app, as the API host's identity seeding and settings see it.</summary>
internal sealed class BffClientModel
{
    public string Name { get; init; } = "";

    public string ClientId { get; init; } = "";

    public int Port { get; init; }

}

/// <summary>An upstream service of the BFFs besides <c>api</c> (<c>Bff:Services:{name}</c>).</summary>
internal sealed class BffServiceModel
{
    public string Name { get; init; } = "";

    public string Address { get; init; } = "";

    /// <summary>A logical address (<c>https+http://catalog</c>) that service discovery resolves.</summary>
    public bool UsesDiscovery => Address.Contains("+http", StringComparison.Ordinal) || Address.StartsWith("http+", StringComparison.Ordinal);
}

/// <summary>One module's typed client in a BFF (<c>ApiClients/{Module}Api.cs</c>).</summary>
internal sealed class BffModuleApiModel
{
    public string ProjectName { get; init; } = "";

    public string ModuleName { get; init; } = "";

    public string ModuleLower => ModuleName.ToLowerInvariant();

    /// <summary>The upstream service: the module's own when <c>Bff:Services</c> has one named after it, else <c>api</c>.</summary>
    public string Service { get; init; } = BffClients.DefaultService;

    public IReadOnlyList<string> Entities { get; init; } = [];

    /// <summary>The entity methods, rendered by <see cref="BffApiClients"/> (<c>bff/ModuleApiEntity</c> per entity).</summary>
    public string EntityMethods { get; set; } = "";

    /// <summary>The model for one entity's methods.</summary>
    public object EntityModel(string entity) => new
    {
        EntityName = entity,
        Plural = CodeGen.Pluralize(entity),
        RouteName = CodeGen.Pluralize(entity).ToLowerInvariant(),
        ModuleLower,
    };

    /// <summary>The service a module's client uses, given the BFF's upstream services.</summary>
    public static string ServiceFor(string module, IEnumerable<string> services)
        => services.Contains(module.ToLowerInvariant(), StringComparer.OrdinalIgnoreCase) ? module.ToLowerInvariant() : BffClients.DefaultService;
}

/// <summary>Model for the templates under <c>Templates/bff/</c>: one BFF host project.</summary>
internal sealed class BffHostModel
{
    public string RootNamespace { get; init; } = "";

    public string AppName { get; init; } = "";

    public string AppNameLower => AppName.ToLowerInvariant();

    public string FrameworkVersion => Services.FrameworkVersion.Current;

    public string UserSecretsId { get; init; } = "";

    /// <summary><c>web</c>, <c>mobile</c> or <c>partner</c>.</summary>
    public string ClientName { get; init; } = "";

    public string ClientPascal => char.ToUpperInvariant(ClientName[0]) + ClientName[1..];

    public bool IsWeb => ClientName == "web";

    public bool IsMobile => ClientName == "mobile";

    public string ProjectName => $"{RootNamespace}.Bff.{ClientPascal}";

    public string ClientId { get; init; } = "";

    public int Port { get; init; }

    public string ApiBaseUrl => BffClients.ApiBaseUrl;

    public string AuthServer { get; init; } = "";

    public string Authority { get; init; } = "";

    /// <summary><c>Oidc</c> (code + PKCE at the auth server's login page) or <c>Password</c> (the API has no login page).</summary>
    public string LoginMode { get; init; } = "Oidc";

    public bool UseRedisCache { get; init; }

    public bool EnableCorrelation { get; init; }

    public bool EnableSecurityHeaders { get; init; }

    public bool EnableSecretsGuard { get; init; }

    public bool HasExample { get; init; }

    public string ExampleModule { get; init; } = "";

    public string ExampleModuleLower => ExampleModule.ToLowerInvariant();

    public string ExampleModuleCamel => ExampleModule.Length == 0 ? "" : char.ToLowerInvariant(ExampleModule[0]) + ExampleModule[1..];

    /// <summary>The example entity's route, e.g. <c>products</c>.</summary>
    public string ExampleRoute { get; init; } = "";

    public string ExampleRoutePascal => ExampleRoute.Length == 0 ? "" : char.ToUpperInvariant(ExampleRoute[0]) + ExampleRoute[1..];

    /// <summary>The project's path in the <c>.slnx</c>.</summary>
    public string SolutionPath => $"src/Bff/{ProjectName}/{ProjectName}.csproj";

    /// <summary>Upstream services besides <c>api</c>; each also gets a <c>/api/{name}</c> passthrough route.</summary>
    public IReadOnlyList<BffServiceModel> UpstreamServices { get; init; } = [];

    /// <summary>On when an upstream address is logical (<c>https+http://catalog</c>).</summary>
    public bool UseServiceDiscovery => UpstreamServices.Any(s => s.UsesDiscovery);

    /// <summary>The module clients generated under <c>ApiClients/</c> (registered by <c>ApiClientRegistration</c>).</summary>
    public IReadOnlyList<BffModuleApiModel> Modules { get; set; } = [];

    /// <summary>The example module's service (for the <c>/home</c> aggregator's comment and the API client).</summary>
    public string ExampleService => BffModuleApiModel.ServiceFor(ExampleModule, UpstreamServices.Select(x => x.Name));

    public static BffHostModel For(AppModel app, string client, int index) => new()
    {
        RootNamespace = app.RootNamespace,
        AppName = app.AppName,
        UserSecretsId = app.UserSecretsId,
        ClientName = client,
        ClientId = $"{app.AppNameLower}-{client}",
        Port = BffClients.FirstPort + index,
        AuthServer = BffClients.AuthServer(app.Auth),
        Authority = BffClients.Authority(app.Auth, app.AppNameLower),
        // The local token server can only run the code flow where the API hosts a login page (a webapp host);
        // every external provider has its own hosted login page.
        LoginMode = app.UseOpenIddict ? "Password" : "Oidc",
        UseRedisCache = app.UseRedisCache,
        EnableCorrelation = app.EnableCorrelation,
        EnableSecurityHeaders = app.EnableSecurityHeaders,
        EnableSecretsGuard = app.EnableSecretsGuard,
        HasExample = !app.NoExample,
        ExampleModule = app.ExampleModule,
        ExampleRoute = app.ExampleRoute,
        UpstreamServices = app.BffServices,
        Modules = app.NoExample
            ? []
            :
            [
                new BffModuleApiModel
                {
                    ProjectName = $"{app.RootNamespace}.Bff.{char.ToUpperInvariant(client[0]) + client[1..]}",
                    ModuleName = app.ExampleModule,
                    Service = BffModuleApiModel.ServiceFor(app.ExampleModule, app.BffServices.Select(x => x.Name)),
                    Entities = [app.ExampleEntity],
                },
            ],
    };
}
