namespace Modulus.Identity;

using Microsoft.Extensions.DependencyInjection;
using Modulus.Identity.Abstractions;
using OpenIddict.Abstractions;
using OpenIddict.Validation;
using static OpenIddict.Validation.OpenIddictValidationEvents;

/// <summary>
/// Rejects an access token whose session has ended or timed out (and records that the session is in use). Tokens without a
/// <c>sid</c> claim (issued before the session registry was switched on) and hosts without the registry are not affected.
/// </summary>
internal sealed class SessionValidationHandler(IServiceProvider services) : IOpenIddictValidationHandler<ValidateTokenContext>
{
    public static OpenIddictValidationHandlerDescriptor Descriptor { get; } =
        OpenIddictValidationHandlerDescriptor.CreateBuilder<ValidateTokenContext>()
            .UseScopedHandler<SessionValidationHandler>()
            .SetOrder(OpenIddictValidationHandlers.Protection.ValidateTokenEntry.Descriptor.Order + 1_000)
            .SetType(OpenIddictValidationHandlerType.Custom)
            .Build();

    public async ValueTask HandleAsync(ValidateTokenContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.IsRejected
            || context.Principal?.GetClaim(TokenPrincipalFactory.SessionClaim) is not { Length: > 0 } sid
            || services.GetService<ISessionRegistry>() is not { } registry)
            return;

        // A sid that is not a session id cannot have been issued by this server.
        var state = Guid.TryParse(sid, out var sessionId)
            ? await registry.TouchAsync(sessionId, ct: context.CancellationToken)
            : SessionState.Unknown;
        if (state != SessionState.Active)
        {
            context.Reject(
                error: OpenIddictConstants.Errors.InvalidToken,
                description: state == SessionState.IdleExpired ? "The session timed out." : "The session has ended.");
        }
    }
}
