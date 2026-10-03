namespace Modulus.GraphQL.Tests;

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using global::GraphQL;
using global::GraphQL.Types;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions.Exceptions;
using Modulus.Mediator.Abstractions;
using Modulus.Mediator.Extensions;

// ── A "Catalog" module: mediator queries/commands, a graph type and a contributor ─────────────

public sealed record ProductDto(Guid Id, string Name);

public sealed record GetProductsQuery : IQuery<IReadOnlyList<ProductDto>>;

public sealed record GetProductQuery(Guid Id) : IQuery<ProductDto>;

public sealed record CreateProductCommand(string Name) : ICommand<Guid>;

public sealed class ProductStore
{
    public ConcurrentDictionary<Guid, ProductDto> Items { get; } = new();
}

public sealed class GetProductsHandler(ProductStore store) : IQueryHandler<GetProductsQuery, IReadOnlyList<ProductDto>>
{
    public Task<IReadOnlyList<ProductDto>> HandleAsync(GetProductsQuery query, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<ProductDto>>(store.Items.Values.OrderBy(p => p.Name, StringComparer.Ordinal).ToList());
}

public sealed class GetProductHandler(ProductStore store) : IQueryHandler<GetProductQuery, ProductDto>
{
    public Task<ProductDto> HandleAsync(GetProductQuery query, CancellationToken ct)
        => store.Items.TryGetValue(query.Id, out var item)
            ? Task.FromResult(item)
            : throw new NotFoundException($"Product {query.Id} was not found.");
}

public sealed class CreateProductHandler(ProductStore store) : ICommandHandler<CreateProductCommand, Guid>
{
    public Task<Guid> HandleAsync(CreateProductCommand command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
            throw new ValidationException(["Name: must not be empty"]);
        var id = Guid.NewGuid();
        store.Items[id] = new ProductDto(id, command.Name);
        return Task.FromResult(id);
    }
}

public sealed class ProductGraphType : ObjectGraphType<ProductDto>
{
    public ProductGraphType()
    {
        Name = "Product";
        Field(x => x.Id, type: typeof(NonNullGraphType<IdGraphType>));
        Field(x => x.Name);
    }
}

public sealed class CatalogGraphQL : IGraphQLContributor
{
    public void ConfigureQuery(ObjectGraphType query)
    {
        query.Field<NonNullGraphType<ListGraphType<NonNullGraphType<ProductGraphType>>>>("products")
            .AuthorizeWithPolicy(GraphQLTestHost.ReadPolicy)
            .ResolveAsync(async context => await context.QueryAsync(new GetProductsQuery()));

        query.Field<ProductGraphType>("product")
            .Argument<NonNullGraphType<IdGraphType>>("id")
            .AuthorizeWithPolicy(GraphQLTestHost.ReadPolicy)
            .ResolveAsync(async context => await context.QueryAsync(new GetProductQuery(context.GetGuidArgument("id"))));

        query.Field<StringGraphType>("fail")
            .Argument<NonNullGraphType<StringGraphType>>("kind")
            .Resolve(context => context.GetArgument<string>("kind") switch
            {
                "forbidden" => throw new ForbiddenException("x:y:z"),
                "conflict" => throw new ConflictException("taken"),
                "feature" => throw new FeatureDisabledException("Beta"),
                "unauthorized" => throw new UnauthorizedException(),
                _ => throw new InvalidOperationException("secret connection string"),
            });

        // Two fields sharing one scoped service that must not be used concurrently (like a DbContext).
        query.Field<NonNullGraphType<IntGraphType>>("first")
            .ResolveAsync(async context => await context.RequestServices!.GetRequiredService<SingleUseService>().UseAsync());
        query.Field<NonNullGraphType<IntGraphType>>("second")
            .ResolveAsync(async context => await context.RequestServices!.GetRequiredService<SingleUseService>().UseAsync());
    }

    public void ConfigureMutation(ObjectGraphType mutation)
        => mutation.Field<NonNullGraphType<IdGraphType>>("createProduct")
            .Argument<NonNullGraphType<StringGraphType>>("name")
            .AuthorizeWithPolicy(GraphQLTestHost.ManagePolicy)
            .ResolveAsync(async context => await context.SendAsync(new CreateProductCommand(context.GetArgument<string>("name"))));
}

/// <summary>Throws when two callers use it at once, the way EF Core's DbContext does.</summary>
public sealed class SingleUseService
{
    private int _active;
    private int _calls;

    public async Task<int> UseAsync()
    {
        if (Interlocked.Increment(ref _active) > 1)
            throw new InvalidOperationException("A second operation was started on this context instance.");
        try
        {
            await Task.Delay(20);
            return Interlocked.Increment(ref _calls);
        }
        finally
        {
            Interlocked.Decrement(ref _active);
        }
    }
}

// ── An "Inventory" module extending Catalog's Product with a batched field ─────────────────────

public sealed class StockCounter
{
    public List<IReadOnlyList<Guid>> Batches { get; } = [];
}

public static class InventoryGraphQL
{
    public static int StockOf(Guid id) => id.ToByteArray()[0];

    public static IServiceCollection AddInventoryGraphQL(this IServiceCollection services)
        => services.ExtendGraphType<ProductGraphType>(product => product
            .Field<NonNullGraphType<IntGraphType>>("stock")
            .Resolve(context => context.LoadBatch<Guid, int>("inventory.stock", ((ProductDto)context.Source!).Id,
                (ids, sp, _) =>
                {
                    sp.GetRequiredService<StockCounter>().Batches.Add(ids);
                    return Task.FromResult<IDictionary<Guid, int>>(ids.ToDictionary(id => id, StockOf));
                })));
}

// ── Host ───────────────────────────────────────────────────────────────────────────────────

public sealed class GraphQLTestHost : IAsyncDisposable
{
    public const string ReadPolicy = "catalog:read";
    public const string ManagePolicy = "catalog:manage";

    private GraphQLTestHost(WebApplication app) => App = app;

    public WebApplication App { get; }

    public ProductStore Store => App.Services.GetRequiredService<ProductStore>();

    public static async Task<GraphQLTestHost> StartAsync(
        Action<ModulusGraphQLOptions>? options = null,
        Action<IServiceCollection>? services = null,
        Action<WebApplication>? map = null,
        bool scanAssembly = false)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<ProductStore>();
        builder.Services.AddSingleton<StockCounter>();
        builder.Services.AddScoped<SingleUseService>();
        builder.Services.AddMediator();
        builder.Services.AddMediatorHandlers(typeof(GraphQLTestHost).Assembly);
        builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, HeaderAuthHandler>("Test", null);
        builder.Services.AddAuthorization(o =>
        {
            o.AddPolicy(ReadPolicy, p => p.RequireClaim("permission", ReadPolicy));
            o.AddPolicy(ManagePolicy, p => p.RequireClaim("permission", ManagePolicy));
            o.AddPolicy("mobile", p => p.RequireClaim("client", "mobile"));
        });

        if (scanAssembly)
            builder.Services.AddModulusGraphQL(builder.Configuration, typeof(GraphQLTestHost).Assembly);
        else
            builder.Services.AddModulusGraphQL(builder.Configuration).AddGraphQLContributor<CatalogGraphQL>();
        if (options is not null)
            builder.Services.Configure(options);
        services?.Invoke(builder.Services);

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        if (map is null)
            app.MapModulusGraphQL();
        else
            map(app);
        await app.StartAsync();
        return new GraphQLTestHost(app);
    }

    public HttpClient Client(params string[] permissions)
    {
        var client = App.GetTestClient();
        if (permissions.Length > 0)
        {
            client.DefaultRequestHeaders.Add("X-Test-User", "alice");
            foreach (var permission in permissions)
                client.DefaultRequestHeaders.Add("X-Test-Claim", "permission=" + permission);
        }

        return client;
    }

    public static async Task<(HttpStatusCode Status, JsonElement Body)> PostAsync(HttpClient client, string query, object? variables = null, string path = "/graphql")
    {
        using var response = await client.PostAsJsonAsync(path, new { query, variables });
        var text = await response.Content.ReadAsStringAsync();
        var body = text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone();
        return (response.StatusCode, body);
    }

    public ValueTask DisposeAsync() => App.DisposeAsync();

    private sealed class HeaderAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-User", out var user))
                return Task.FromResult(AuthenticateResult.NoResult());

            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, user.ToString()) };
            foreach (var claim in Request.Headers["X-Test-Claim"].OfType<string>())
            {
                var separator = claim.IndexOf('=', StringComparison.Ordinal);
                claims.Add(new Claim(claim[..separator], claim[(separator + 1)..]));
            }

            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }
}

internal static class RouteGroupTestExtensions
{
    public static IEndpointConventionBuilder MapMobileGraphQL(this WebApplication app)
        => app.MapGroup("/mobile").RequireAuthorization("mobile").MapModulusGraphQL();
}
