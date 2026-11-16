using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Modulus.Authorization.Extensions;
using Modulus.Core.Abstractions;
using Modulus.Core.Null;

namespace Modulus.Messaging.Management;

/// <summary>
/// Operator API for messaging (FR-09, the API half of the BRS replay acceptance). It moves the parked messages of an
/// endpoint's <c>_error</c> queue back to the endpoint, through <see cref="IMessageErrorQueue"/>, so an operator can
/// recover from a failed deployment without redeploying. Every endpoint needs <see cref="ReplayPermission"/>.
/// </summary>
public static class MessagingManagementExtensions
{
    /// <summary>The permission that replays parked messages. Critical: a replay runs business handlers again.</summary>
    public const string ReplayPermission = "messaging:errors:replay";

    /// <summary>Largest number of messages one replay call moves.</summary>
    public const int MaxReplayLimit = 1000;

    /// <summary>Default number of messages one replay call moves.</summary>
    public const int DefaultReplayLimit = 100;

    /// <summary>Declares <see cref="ReplayPermission"/> in the registry and makes the authorization services available.</summary>
    public static IServiceCollection AddModulusMessagingManagement(this IServiceCollection services)
    {
        services.AddAuthorization();

        // TryAdd, so the host's identity backend is kept.
        services.TryAddScoped<ICurrentUser, NullCurrentUser>();

        return services.AddPermissions("Modulus.Messaging", registry =>
            registry.Add(
                ReplayPermission,
                "Replay the parked messages of an endpoint back to it.",
                null,
                PermissionSensitivity.Critical));
    }

    /// <summary>Maps <c>POST {prefix}/{endpoint}/replay?limit=</c>, guarded by <see cref="ReplayPermission"/>.</summary>
    public static RouteGroupBuilder MapModulusMessagingErrors(
        this IEndpointRouteBuilder endpoints,
        string prefix = "/messaging/errors")
    {
        var group = endpoints.MapGroup(prefix)
            .RequireAuthorization(ReplayPermission)
            .WithTags("Messaging Errors");

        group.MapPost("/{endpoint}/replay", async (
            string endpoint,
            int? limit,
            IMessageErrorQueue errors,
            ICurrentUser currentUser,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                return Results.BadRequest("The endpoint name is required.");
            }

            var count = limit ?? DefaultReplayLimit;
            if (count is < 1 or > MaxReplayLimit)
            {
                return Results.BadRequest($"limit must be between 1 and {MaxReplayLimit}.");
            }

            var moved = await errors.ReplayAsync(endpoint, count, cancellationToken);
            loggerFactory.CreateLogger("Modulus.Messaging.Management").LogInformation(
                "Replayed {Moved} parked messages of {Endpoint} by {User}",
                moved,
                endpoint,
                currentUser.UserId?.ToString() ?? "unknown");
            return Results.Ok(new MessageErrorReplayResponse(endpoint, moved));
        })
        .WithSummary("Replay parked messages of an endpoint");

        return group;
    }
}

/// <summary>The result of a replay call: how many parked messages went back to <see cref="Endpoint"/>.</summary>
public sealed record MessageErrorReplayResponse(string Endpoint, int Moved);
