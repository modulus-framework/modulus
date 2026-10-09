namespace Modulus.AI.Connector.Tests;

using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Ai;
using Modulus.Core.Abstractions.Compliance;
using Modulus.Core.Abstractions.Entities;
using Modulus.Core.Abstractions.Exceptions;
using Modulus.Mediator.Abstractions;
using Modulus.Mediator.Abstractions.Attributes;
using Modulus.Mediator.Extensions;
using Modulus.MultiTenancy;

public sealed class SupplierDto
{
    public string Name { get; init; } = string.Empty;

    [Classified(FieldClassification.Restricted)]
    public decimal Margin { get; init; }
}

public sealed class ProductDto
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    [Classified(FieldClassification.Confidential)]
    public decimal Cost { get; init; }

    [SecretData]
    public string? ApiToken { get; init; }

    [PersonalInformation]
    public string? Owner { get; init; }

    public SupplierDto? Supplier { get; init; }
}

[AiCapability("Test.Catalog.Product.Search", "Finds products by name.", ResourceType = "Catalog.Product")]
[RequirePermission("catalog:read")]
public sealed record SearchProducts(string? Text = null) : IQuery<IReadOnlyList<ProductDto>>;

[AiResource("Catalog.Product", "A product in the catalog.", DeepLink = "/products/{id}", TitleField = "Name", BatchLookup = typeof(GetProductsByIds))]
[RequirePermission("catalog:read")]
public sealed record GetProduct(Guid Id) : IQuery<ProductDto>;

/// <summary>The batch version of <see cref="GetProduct"/> (one query for a page of the index).</summary>
[RequirePermission("catalog:read")]
public sealed record GetProductsByIds(IReadOnlyCollection<Guid> Ids) : IQuery<IReadOnlyList<ProductDto>>;

/// <summary>Counts how the product lookups were run (registered only by tests that look).</summary>
public sealed class LookupLog
{
    public int Single;
    public int Batch;
    public List<int> BatchSizes { get; } = [];
}

[AiCapability("Test.Catalog.Product.Count", "Counts the products.")]
public sealed record CountProducts : IQuery<int>;

/// <summary>Never answers (waits for the connector's call timeout): the conformance kit's slow capability.</summary>
[AiCapability("Test.Catalog.Slow.Run", "A deliberately slow query.")]
public sealed record SlowThing : IQuery<int>;

public sealed class SlowThingHandler : IQueryHandler<SlowThing, int>
{
    public async Task<int> HandleAsync(SlowThing query, CancellationToken ct)
    {
        await Task.Delay(Timeout.Infinite, ct);
        return 0;
    }
}

public sealed class SearchProductsHandler(SearchProductsFault? fault = null) : IQueryHandler<SearchProducts, IReadOnlyList<ProductDto>>
{
    public Task<IReadOnlyList<ProductDto>> HandleAsync(SearchProducts query, CancellationToken ct)
    {
        fault?.Throw(query.Text);
        return Task.FromResult<IReadOnlyList<ProductDto>>(
            [.. Catalog.Products.Where(p => query.Text is null || p.Name.Contains(query.Text, StringComparison.OrdinalIgnoreCase))]);
    }
}

/// <summary>Makes <see cref="SearchProductsHandler"/> fail on some input (registered only by tests that want a broken search).</summary>
public sealed class SearchProductsFault(Func<string?, Exception?> fault)
{
    public void Throw(string? text)
    {
        if (fault(text) is { } exception)
            throw exception;
    }
}

public sealed class GetProductHandler(LookupLog? log = null) : IQueryHandler<GetProduct, ProductDto>
{
    public Task<ProductDto> HandleAsync(GetProduct query, CancellationToken ct)
    {
        if (log is not null)
            Interlocked.Increment(ref log.Single);
        return Task.FromResult(Catalog.Products.FirstOrDefault(p => p.Id == query.Id)
            ?? throw new NotFoundException($"Product {query.Id} not found."));
    }
}

public sealed class GetProductsByIdsHandler(LookupLog? log = null) : IQueryHandler<GetProductsByIds, IReadOnlyList<ProductDto>>
{
    public Task<IReadOnlyList<ProductDto>> HandleAsync(GetProductsByIds query, CancellationToken ct)
    {
        if (log is not null)
        {
            Interlocked.Increment(ref log.Batch);
            lock (log.BatchSizes)
                log.BatchSizes.Add(query.Ids.Count);
        }

        return Task.FromResult<IReadOnlyList<ProductDto>>([.. Catalog.Products.Where(p => query.Ids.Contains(p.Id))]);
    }
}

public sealed class CountProductsHandler : IQueryHandler<CountProducts, int>
{
    public Task<int> HandleAsync(CountProducts query, CancellationToken ct) => Task.FromResult(Catalog.Products.Count);
}

public static class Catalog
{
    public static readonly Guid WidgetId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid GadgetId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public static readonly IReadOnlyList<ProductDto> Products =
    [
        new() { Id = WidgetId, Name = "Widget", Cost = 3.5m, ApiToken = "tok-1", Owner = "ann", Supplier = new() { Name = "Acme", Margin = 0.4m } },
        new() { Id = GadgetId, Name = "Gadget", Cost = 7m, ApiToken = "tok-2", Owner = "bob" },
    ];
}

/// <summary>Accounts the tests know; permissions come from roles.</summary>
public sealed class TestUsers : IAiConnectorUserResolver
{
    public static readonly Guid Reader = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    public static readonly Guid NoRights = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
    public static readonly Guid Auditor = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003");

    public static readonly IReadOnlyDictionary<string, string[]> RolePermissions = new Dictionary<string, string[]>
    {
        ["reader"] = ["catalog:read"],
        ["AiIndexer"] = ["catalog:read"],
        ["auditor"] = ["audit:view"],
    };

    public Task<AiConnectorUser?> ResolveAsync(AiUserLookup lookup, CancellationToken ct = default)
    {
        AiConnectorUser? user = Guid.TryParse(lookup.Value, out var id) switch
        {
            true when id == Reader => new(Reader, "reader", "reader@test", ["reader"]),
            true when id == NoRights => new(NoRights, "nobody", null, []),
            true when id == Auditor => new(Auditor, "auditor", null, ["auditor", "reader"]),
            _ => null,
        };
        return Task.FromResult(user);
    }
}

/// <summary>The request principal as <see cref="ICurrentUser"/>, permissions by role.</summary>
public sealed class TestCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public Guid? UserId => Guid.TryParse(Principal?.FindFirst("sub")?.Value, out var id) ? id : null;

    public string? UserName => Principal?.FindFirst("name")?.Value;

    public string? Email => Principal?.FindFirst("email")?.Value;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public IReadOnlyList<string> Permissions =>
    [
        .. (Principal?.FindAll("role") ?? [])
            .SelectMany(r => TestUsers.RolePermissions.TryGetValue(r.Value, out var p) ? p : [])
            .Distinct(),
    ];

    public bool IsInRole(string role) => Principal?.HasClaim("role", role) == true;

    public bool HasPermission(string permission) => Permissions.Contains(permission);
}

/// <summary>Knows one active company.</summary>
public sealed class TestTenantRestorer : ITenantContextRestorer
{
    public static readonly Guid Company = Guid.Parse("cccccccc-0000-0000-0000-000000000001");

    public ValueTask<TenantInfo> VerifyAsync(Guid tenantId, CancellationToken ct = default)
        => tenantId == Company ? ValueTask.FromResult(new TenantInfo(tenantId, "acme")) : throw new TenantContextRejectedException(tenantId);
}

/// <summary>Records what the platform received and answers with queued status codes (default 200).</summary>
public sealed class FakePlatform : HttpMessageHandler
{
    private readonly ConcurrentQueue<System.Net.HttpStatusCode> _answers = new();

    public ConcurrentQueue<(string Path, string? Authorization, string Body)> Received { get; } = new();

    public SemaphoreSlim Succeeded { get; } = new(0);

    public void Enqueue(params System.Net.HttpStatusCode[] answers)
    {
        foreach (var answer in answers)
            _answers.Enqueue(answer);
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Received.Enqueue((request.RequestUri!.AbsolutePath, request.Headers.Authorization?.ToString(), body));
        var status = _answers.TryDequeue(out var queued) ? queued : System.Net.HttpStatusCode.OK;
        if ((int)status < 300)
            Succeeded.Release();
        return new HttpResponseMessage(status);
    }
}

/// <summary>A TestServer app with the connector, a signing key standing in for the platform, and helpers.</summary>
public sealed class ConnectorTestHost : IAsyncDisposable
{
    public const string ApiKey = "test-api-key";
    public const string Issuer = "https://ai.test";
    public const string PlatformTenant = "pt-1";
    public const string Instance = "inst-1";
    public const string CompanyInstance = "inst-company";

    private readonly RSA _rsa;
    private readonly WebApplication _app;

    private ConnectorTestHost(WebApplication app, RSA rsa, FakePlatform platform)
    {
        _app = app;
        _rsa = rsa;
        Platform = platform;
        Client = app.GetTestClient();
    }

    public HttpClient Client { get; }

    public IServiceProvider Services => _app.Services;

    public FakePlatform Platform { get; }

    public InMemoryTenantMembershipStore Memberships => (InMemoryTenantMembershipStore)Services.GetRequiredService<ITenantMembershipStore>();

    public RecordingAuditLog Audit => (RecordingAuditLog)Services.GetRequiredService<ISecurityAuditLog>();

    public static async Task<ConnectorTestHost> StartAsync(
        Action<IDictionary<string, string?>>? settings = null,
        bool resolveUsers = true,
        Action<IServiceCollection>? configure = null,
        Action<AiConnectorBuilder>? connector = null)
    {
        var rsa = RSA.Create(2048);
        var parameters = rsa.ExportParameters(false);
        var jwks = $$"""{"keys":[{"kty":"RSA","use":"sig","alg":"RS256","kid":"k1","n":"{{Base64UrlEncoder.Encode(parameters.Modulus)}}","e":"{{Base64UrlEncoder.Encode(parameters.Exponent)}}"}]}""";

        var values = new Dictionary<string, string?>
        {
            ["Ai:Connector:AppName"] = "Test ERP",
            ["Ai:Connector:ApiKeyHashes:0"] = AiApiKeys.Hash(ApiKey),
            ["Ai:Connector:Platform:Issuer"] = Issuer,
            ["Ai:Connector:Platform:SigningKeys"] = jwks,
            ["Ai:Connector:Platform:BaseUrl"] = "https://platform.test",
            ["Ai:Connector:Platform:ApiKey"] = "platform-key",
            ["Ai:Connector:Platform:RevocationMaxBackoff"] = "00:00:00.020",
            ["Ai:Connector:Instances:0:AppInstanceId"] = Instance,
            ["Ai:Connector:Instances:0:PlatformTenantId"] = PlatformTenant,
            ["Ai:Connector:Instances:1:AppInstanceId"] = CompanyInstance,
            ["Ai:Connector:Instances:1:PlatformTenantId"] = PlatformTenant,
            ["Ai:Connector:Instances:1:TenantId"] = TestTenantRestorer.Company.ToString(),
            ["Ai:Connector:PublicBaseUrl"] = "https://erp.test",
        };
        settings?.Invoke(values);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(values);

        var platform = new FakePlatform();
        var services = builder.Services;
        services.AddHttpContextAccessor();
        services.AddSingleton<ISecurityAuditLog, RecordingAuditLog>();
        services.AddScoped<ICurrentUser, TestCurrentUser>();
        services.AddSingleton<ITenantContextRestorer, TestTenantRestorer>();
        services.AddSingleton<ITenantMembershipStore, InMemoryTenantMembershipStore>();
        services.AddMediator(o =>
        {
            o.EnableCaching = false;
            o.EnableTransaction = false;
        });
        services.AddMediatorHandlers(typeof(ConnectorTestHost).Assembly);
        services.AddModulusAiConnector(builder.Configuration, ai =>
        {
            if (resolveUsers)
                ai.UseUserResolver<TestUsers>();
            connector?.Invoke(ai);
        });
        services.AddHttpClient("Modulus.AI.Connector.Revocation").ConfigurePrimaryHttpMessageHandler(() => platform);
        services.AddHttpClient("Modulus.AI.Connector.ChangeHints").ConfigurePrimaryHttpMessageHandler(() => platform);
        configure?.Invoke(services);

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapModulusAiConnector();
        await app.StartAsync();
        return new ConnectorTestHost(app, rsa, platform);
    }

    /// <summary>A signed envelope; <paramref name="tweak"/> changes the descriptor before signing.</summary>
    public string Envelope(Guid? user = null, string instance = Instance, Action<SecurityTokenDescriptor>? tweak = null, RSA? key = null)
    {
        var now = DateTime.UtcNow;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = instance,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddSeconds(60),
            Claims = new Dictionary<string, object>
            {
                ["jti"] = Guid.NewGuid().ToString("N"),
                ["sub"] = (user ?? TestUsers.Reader).ToString(),
                [AiEnvelopeClaims.TenantId] = PlatformTenant,
                [AiEnvelopeClaims.AppInstanceId] = instance,
                [AiEnvelopeClaims.CorrelationId] = "corr-1",
            },
            SigningCredentials = new SigningCredentials(new RsaSecurityKey(key ?? _rsa) { KeyId = "k1" }, SecurityAlgorithms.RsaSha256),
        };
        tweak?.Invoke(descriptor);
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    public HttpRequestMessage Request(HttpMethod method, string path, string? envelope, string? json = null, string? apiKey = ApiKey)
    {
        var request = new HttpRequestMessage(method, "/_ai/connector" + path);
        if (apiKey is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("ApiKey", apiKey);
        if (envelope is not null)
            request.Headers.Add("AiPlatform-Envelope", envelope);
        if (json is not null)
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return request;
    }

    public Task<HttpResponseMessage> PostAsync(string path, string? json, string? envelope = null)
        => Client.SendAsync(Request(HttpMethod.Post, path, envelope ?? Envelope(), json));

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
        _rsa.Dispose();
    }
}

public sealed class RecordingAuditLog : ISecurityAuditLog
{
    public ConcurrentQueue<SecurityAuditEvent> Events { get; } = new();

    public void Record(SecurityAuditEvent auditEvent) => Events.Enqueue(auditEvent);
}
