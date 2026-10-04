namespace Modulus.Realtime;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions.Security;

/// <summary>
/// Lists every registered topic in the startup security guard's report: a topic with a permission or an authorization
/// callback is policed, any other inherits the realtime endpoint's sign-in requirement (or is anonymous when
/// <see cref="ModulusRealtimeOptions.RequireAuthenticatedUser"/> is off). Pushed events are not listed: their audience is
/// computed per event and always stays inside the publishing tenant.
/// </summary>
internal sealed class RealtimeSecuritySurface : ISecuritySurfaceContributor
{
    public IEnumerable<SecuritySurfaceEntry> Describe(IServiceProvider services)
    {
        var signedIn = services.GetRequiredService<IOptions<ModulusRealtimeOptions>>().Value.RequireAuthenticatedUser;
        foreach (var topic in services.GetServices<RealtimeTopicDefinition>())
        {
            List<string> policies = [];
            if (topic.Permission is { } permission)
                policies.Add(permission);
            if (topic.Authorize is not null)
                policies.Add("(callback)");

            var access = policies.Count > 0 ? SecuritySurfaceAccess.Policed
                : signedIn ? SecuritySurfaceAccess.Inherited
                : SecuritySurfaceAccess.Anonymous;
            yield return new SecuritySurfaceEntry("realtime", $"topic {topic.Pattern}", access, policies);
        }
    }
}
