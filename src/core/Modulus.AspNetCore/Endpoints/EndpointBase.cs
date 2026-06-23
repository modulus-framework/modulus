using Microsoft.AspNetCore.Routing;

namespace Modulus.AspNetCore.Endpoints;

using Microsoft.AspNetCore.Mvc;

/// <summary>
/// Controller-style base class (ardalis/ApiEndpoints pattern).
/// Inherits ControllerBase. Implements IEndpoint as a no-op
/// (routes declared via [Http*] attributes, mapped by MapControllers()).
/// </summary>
[ApiController]
public abstract class EndpointBase<TRequest, TResponse>
    : ControllerBase, IEndpoint
{
    public abstract Task<ActionResult<TResponse>> HandleAsync(
        TRequest request,
        CancellationToken cancellationToken = default);

    /// <inheritdoc/>
    public virtual void MapEndpoint(IEndpointRouteBuilder app) { }
}

[ApiController]
public abstract class EndpointBase<TRequest>
    : ControllerBase, IEndpoint
{
    public abstract Task<IActionResult> HandleAsync(
        TRequest request,
        CancellationToken cancellationToken = default);

    public virtual void MapEndpoint(IEndpointRouteBuilder app) { }
}