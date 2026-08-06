using System.Reflection;
using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using Modulus.Mediator.Abstractions.Attributes;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Shared.Application.Behaviors;

internal sealed class AuthorizationPipelineBehavior<TRequest, TResponse>(
    ICurrentUser currentUser)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : class
    where TResponse : Result
{
    private static readonly RequirePermissionAttribute? s_attribute =
        typeof(TRequest).GetCustomAttribute<RequirePermissionAttribute>();

    public async Task<TResponse> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var attr = s_attribute;

        if (attr is null)
            return await next();

        if (!currentUser.IsAuthenticated)
            return CreateFailure("Authentication required");

        if (!currentUser.HasPermission(attr.Permission))
            return CreateFailure($"Permission required: {attr.Permission}");

        return await next();
    }

    private static TResponse CreateFailure(string message)
    {
        var result = Result.Failure(new Error("Authorization", message, ErrorType.Unauthorized));
        return (TResponse)(object)result;
    }
}
