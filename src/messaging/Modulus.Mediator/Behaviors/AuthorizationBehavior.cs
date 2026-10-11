using System.Reflection;

namespace Modulus.Mediator.Behaviors;

using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Exceptions;
using Modulus.Mediator.Abstractions;
using Modulus.Mediator.Abstractions.Attributes;

public sealed class AuthorizationBehavior<TRequest, TResponse>(
    ICurrentUser currentUser,
    IStepUpGate? stepUp = null)
    : IPipelineBehavior<TRequest, TResponse>
{
    // The attribute is fixed per request type; read it once per closed generic.
    private static readonly RequirePermissionAttribute? s_attr =
        typeof(TRequest).GetCustomAttribute<RequirePermissionAttribute>();

    public Task<TResponse> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken ct)
    {
        var attr = s_attr;

        if (attr is null) return next();

        if (!currentUser.IsAuthenticated)
            throw new UnauthorizedException();

        if (!currentUser.HasPermission(attr.Permission))
            throw new ForbiddenException(attr.Permission);

        // Step-up applies to every entry point that runs a command or query (HTTP, gRPC, GraphQL, the BFF), not only HTTP policies.
        if (stepUp is not null && !stepUp.IsSatisfied(attr.Permission))
            throw new StepUpRequiredException(attr.Permission);

        return next();
    }
}
