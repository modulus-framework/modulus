namespace Modulus.Realtime;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Modulus.Realtime.Transports;

/// <summary>Maps the realtime transports.</summary>
public static class RealtimeEndpointExtensions
{
    /// <summary>
    /// Maps <c>GET {path}/events</c> (SSE, unless <c>Realtime:Sse:Enabled</c> is false) and, when
    /// <c>Realtime:SignalR:Enabled</c>, the <see cref="RealtimeHub"/> at <c>{path}/hub</c>, both behind a signed-in user
    /// unless <c>Realtime:RequireAuthenticatedUser</c> is false. Works on a route group.
    /// </summary>
    public static RouteGroupBuilder MapModulusRealtime(this IEndpointRouteBuilder endpoints, string? path = null)
        => endpoints.MapModulusRealtime<RealtimeHub>(path);

    /// <summary>As <see cref="MapModulusRealtime(IEndpointRouteBuilder, string?)"/>, with your own hub derived from <see cref="RealtimeHub"/>.</summary>
    public static RouteGroupBuilder MapModulusRealtime<THub>(this IEndpointRouteBuilder endpoints, string? path = null)
        where THub : RealtimeHub
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        if (endpoints.ServiceProvider.GetService<IRealtimeDispatcher>() is null)
        {
            throw new InvalidOperationException("Realtime is not registered: call services.AddModulusRealtime(configuration, ...) first.");
        }

        var options = endpoints.ServiceProvider.GetRequiredService<IOptions<ModulusRealtimeOptions>>().Value;
        var group = endpoints.MapGroup(path ?? options.Path).WithTags("Realtime");
        // Off does not mean AllowAnonymous: that would override an enclosing group's policy (a BFF client's).
        // Under a fallback policy, an app that wants anonymous streams opens the returned group with .Loosen(...).
        if (options.RequireAuthenticatedUser)
            group.RequireAuthorization();

        if (options.Sse.Enabled)
        {
            group.MapGet("/events", SseEndpoint.HandleAsync).ExcludeFromDescription();
        }

        if (options.SignalR.Enabled)
        {
            if (endpoints.ServiceProvider.GetService<IHubContext<THub>>() is null)
                throw new InvalidOperationException("Realtime:SignalR:Enabled is true but SignalR is not registered (services.AddSignalR()).");
            group.MapHub<THub>("/hub", o => o.CloseOnAuthenticationExpiration = options.CloseAtTokenExpiry);
        }

        return group;
    }
}
