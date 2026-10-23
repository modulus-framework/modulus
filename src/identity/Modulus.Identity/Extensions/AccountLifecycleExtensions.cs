using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Authorization.Extensions;
using Modulus.Authorization.Fields;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Entities;
using Modulus.Identity.Abstractions;

namespace Modulus.Identity.Extensions;

/// <summary>Body of <c>PUT {id}/lifecycle</c>: the state to move to, and the reason and note a suspension needs.</summary>
/// <param name="To">The state to move to.</param>
/// <param name="Reason">The suspension reason code, or a free-text reason for other moves.</param>
/// <param name="Note">The note a suspension reason requires.</param>
public sealed record AccountLifecycleRequest(AccountLifecycle To, string? Reason = null, string? Note = null);

/// <summary>Body of <c>POST {id}/rehire</c>: why the archived account returns.</summary>
/// <param name="Reason">The reason, kept in the transition history.</param>
public sealed record RehireRequest(string Reason);

/// <summary>
/// The lifecycle admin surface (A1-3): an administrator changes an account's lifecycle, and an approver approves a reviewed rehire.
/// Every route is guarded by <see cref="ManagePermission"/>, which is Critical, so the step-up policy applies when it is on.
/// The approver's own company and role checks are made by <see cref="IRehireReviewService"/>.
/// </summary>
public static class AccountLifecycleExtensions
{
    /// <summary>The permission that guards the lifecycle routes.</summary>
    public const string ManagePermission = "identity:accounts:manage";

    /// <summary>
    /// The permission that reads personal fields (name, email) of an account. Without it the user administration routes mask those
    /// fields (decision D-P3); it is Sensitive, so it can be granted to a role without being Critical.
    /// </summary>
    public const string PersonalDataPermission = "identity:accounts:personal-data";

    /// <summary>Registers the permissions and the field guard for personal fields. Requires <c>AddModulusAuthorization</c> for the permissions to be enforced.</summary>
    public static IServiceCollection AddModulusAccountLifecycleAdmin(this IServiceCollection services)
    {
        services.AddPermissions("Modulus.Identity.Accounts", registry =>
        {
            registry.Add(
                ManagePermission,
                "Change the lifecycle of company accounts (suspend, reinstate, archive) and approve a rehire.",
                null,
                PermissionSensitivity.Critical);
            registry.Add(
                PersonalDataPermission,
                "Read the personal fields of accounts (name, email) in the user administration routes.",
                null,
                PermissionSensitivity.Sensitive);
        });

        var personalData = FieldSecurityProfile.Define(profile => profile.Classification(FieldClassification.Restricted, read: PersonalDataPermission));
        services.AddFieldSecurity<UserSummary>(personalData);
        services.AddFieldSecurity<UserDetail>(personalData);
        return services;
    }

    /// <summary>Maps <c>{prefix}/{id}/lifecycle</c> and <c>{prefix}/{id}/rehire</c> (default prefix <c>/identity/accounts</c>).</summary>
    public static RouteGroupBuilder MapModulusAccountLifecycle(this IEndpointRouteBuilder endpoints, string prefix = "/identity/accounts")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var group = endpoints.MapGroup(prefix).RequireAuthorization(ManagePermission);

        group.MapPut("/{id:guid}/lifecycle", async (Guid id, AccountLifecycleRequest body, ICurrentUser caller, IAccountLifecycleService lifecycle, CancellationToken ct) =>
        {
            if (caller.UserId is not { } actor)
                return Results.Unauthorized();
            try
            {
                var state = await lifecycle.ChangeAsync(new AccountLifecycleChange(id, body.To, actor, body.Reason, body.Note), ct);
                return Results.Ok(new { lifecycle = state.ToString() });
            }
            catch (ArgumentException ex)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { [ex.ParamName ?? "lifecycle"] = [ex.Message] });
            }
        });

        group.MapPost("/{id:guid}/rehire", async (Guid id, RehireRequest body, ICurrentUser caller, IRehireReviewService rehire, CancellationToken ct) =>
        {
            if (caller.UserId is not { } approver)
                return Results.Unauthorized();
            try
            {
                var state = await rehire.ApproveAsync(id, approver, body.Reason, ct);
                return Results.Ok(new { lifecycle = state.ToString() });
            }
            catch (ArgumentException ex)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["reason"] = [ex.Message] });
            }
        });

        return group;
    }
}
