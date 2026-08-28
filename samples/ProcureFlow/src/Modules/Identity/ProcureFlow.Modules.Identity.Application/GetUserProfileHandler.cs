using Modulus.Mediator.Abstractions;

namespace ProcureFlow.Modules.Identity.Application;

/// <summary>Handler for GetUserProfileQuery</summary>
public sealed class GetUserProfileHandler : IQueryHandler<GetUserProfileQuery, object>
{
    public async Task<object> HandleAsync(
        GetUserProfileQuery query,
        CancellationToken ct)
    {
        // TODO: Implement GetUserProfile logic here
        return await Task.FromResult(new { });
    }
}
