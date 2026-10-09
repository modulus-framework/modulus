namespace Modulus.Grpc;

using System.Reflection;
using global::Grpc.AspNetCore.Server;
using global::Grpc.AspNetCore.Web;
using global::Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Modulus.AspNetCore.HealthChecks;
using Modulus.Grpc.Server;
using Modulus.AspNetCore.Security.Policy;
using Modulus.Core.Abstractions.Security;

/// <summary>
/// Hosts gRPC services. A gRPC call goes through the same ASP.NET Core pipeline as an HTTP request, so correlation
/// (<c>X-Correlation-ID</c> metadata), tenant resolution, authentication and authorization (<c>[Authorize]</c>, the
/// <c>module:thing:action</c> permission policies) already apply to it; this adds what is gRPC-specific.
/// </summary>
public static class GrpcServerExtensions
{
    /// <summary>
    /// Adds grpc-web support when <see cref="ModulusGrpcOptions.EnableGrpcWeb"/> is on. Call after <c>UseRouting</c> and
    /// before authentication, CORS and endpoints.
    /// </summary>
    public static IApplicationBuilder UseModulusGrpcWeb(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var options = app.ApplicationServices.GetRequiredService<IOptions<ModulusGrpcOptions>>().Value;
        return options.EnableGrpcWeb ? app.UseGrpcWeb(new GrpcWebOptions { DefaultEnabled = true }) : app;
    }

    /// <summary>
    /// Registers the gRPC server (settings from the <c>Grpc</c> section, <see cref="ModulusGrpcOptions"/>) with
    /// exceptions mapped to status codes (<see cref="GrpcExceptionMapper"/>), the <c>grpc.health.v1</c> service
    /// backed by the health checks (every <c>IModuleHealthCheck</c> included) and the reflection service.
    /// </summary>
    public static IGrpcServerBuilder AddModulusGrpc(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<GrpcServiceOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<ModulusGrpcOptions>().Bind(configuration.GetSection(ModulusGrpcOptions.SectionName));
        services.AddOptions<GrpcServiceOptions>().Configure<IOptions<ModulusGrpcOptions>>((grpc, modulus) =>
        {
            var settings = modulus.Value;
            grpc.EnableDetailedErrors = settings.EnableDetailedErrors;
            if (settings.MaxReceiveMessageSize is { } receive)
                grpc.MaxReceiveMessageSize = receive;
            if (settings.MaxSendMessageSize is { } send)
                grpc.MaxSendMessageSize = send;
            if (!string.IsNullOrWhiteSpace(settings.ResponseCompressionAlgorithm))
            {
                grpc.ResponseCompressionAlgorithm = settings.ResponseCompressionAlgorithm;
                grpc.ResponseCompressionLevel = System.IO.Compression.CompressionLevel.Fastest;
            }
        });

        var builder = services.AddGrpc(grpc =>
        {
            if (!grpc.Interceptors.Any(i => i.Type == typeof(GrpcExceptionInterceptor)))
                grpc.Interceptors.Add<GrpcExceptionInterceptor>();
            configure?.Invoke(grpc);
        });

        // The reflection service describes every service and message, so it is only registered when it is switched on.
        if (configuration.GetSection(ModulusGrpcOptions.SectionName).Get<ModulusGrpcOptions>()?.EnableReflection == true)
            services.AddGrpcReflection();
        services.AddGrpcHealthChecks().AddModulusHealthChecks();
        return builder;
    }

    /// <summary>
    /// Maps every gRPC service implemented in <paramref name="assemblies"/> (a public, non-abstract class deriving from
    /// a <c>protoc</c>-generated <c>…Base</c> class), then <c>grpc.health.v1</c> and, when
    /// <see cref="ModulusGrpcOptions.EnableReflection"/> is on, the reflection service; those two allow anonymous calls.
    /// The returned builder applies conventions to the services only, e.g. <c>.RequireAuthorization()</c>.
    /// </summary>
    public static IEndpointConventionBuilder MapModulusGrpc(this IEndpointRouteBuilder endpoints, params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(assemblies);

        var services = new List<IEndpointConventionBuilder>();
        foreach (var type in FindServiceTypes(assemblies))
        {
            var map = MapServiceMethod.MakeGenericMethod(type);
            services.Add((IEndpointConventionBuilder)map.Invoke(null, [endpoints])!);
        }

        var options = endpoints.ServiceProvider.GetRequiredService<IOptions<ModulusGrpcOptions>>().Value;
        if (options.EnableHealthChecks)
            endpoints.MapGrpcHealthChecksService().Loosen(new LoosenedAttribute("grpc.health.v1 probe for orchestrators and load balancers") { Framework = true });
        if (options.EnableReflection)
            endpoints.MapGrpcReflectionService().Loosen(new LoosenedAttribute("gRPC reflection, opted in through Grpc:EnableReflection") { Framework = true });

        return new CompositeEndpointConventionBuilder(services);
    }

    /// <summary>The gRPC service implementations in <paramref name="assemblies"/>, in name order.</summary>
    internal static IReadOnlyList<Type> FindServiceTypes(IEnumerable<Assembly> assemblies)
        => assemblies
            .Distinct()
            .SelectMany(LoadableTypes)
            .Where(t => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false } && t.IsVisible && IsGrpcService(t))
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .ToList();

    private static bool IsGrpcService(Type type)
    {
        for (var current = type.BaseType; current is not null && current != typeof(object); current = current.BaseType)
        {
            if (current.IsDefined(typeof(BindServiceMethodAttribute), inherit: false))
                return true;
        }

        return false;
    }

    private static IEnumerable<Type> LoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.OfType<Type>();
        }
    }

    private static readonly MethodInfo MapServiceMethod = typeof(GrpcServerExtensions)
        .GetMethod(nameof(MapService), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static GrpcServiceEndpointConventionBuilder MapService<TService>(IEndpointRouteBuilder endpoints)
        where TService : class
        => endpoints.MapGrpcService<TService>();

    private sealed class CompositeEndpointConventionBuilder(IReadOnlyList<IEndpointConventionBuilder> builders) : IEndpointConventionBuilder
    {
        public void Add(Action<EndpointBuilder> convention)
        {
            foreach (var builder in builders)
                builder.Add(convention);
        }

        public void Finally(Action<EndpointBuilder> finallyConvention)
        {
            foreach (var builder in builders)
                builder.Finally(finallyConvention);
        }
    }
}
