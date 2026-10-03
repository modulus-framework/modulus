namespace Modulus.AspNetCore.Security.Policy;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Modulus.Core.Abstractions.Security;

/// <summary>
/// Resolves the security policy of every endpoint and lists what is wrong (pure; the startup guard
/// runs it over the app's <c>EndpointDataSource</c>).
/// <list type="bullet">
/// <item>An endpoint with no authorization data, while no fallback policy is set, is <c>unpoliced</c>.</item>
/// <item>An anonymous endpoint needs a reason: its own <see cref="LoosenedAttribute"/> or an allow-list entry's.</item>
/// <item>A reasoned anonymous endpoint must also be on the allow-list.</item>
/// <item>An anonymous endpoint may not be classified <c>Confidential</c> or <c>Restricted</c>.</item>
/// <item>A network requirement other than <c>Any</c> cannot be enforced yet and is refused.</item>
/// </list>
/// Static-asset endpoints are skipped.
/// </summary>
public static class EndpointSecurityAnalyzer
{
    private const string StaticAssetDescriptorTypeName = "Microsoft.AspNetCore.StaticAssets.StaticAssetDescriptor";

    /// <summary>Analyzes <paramref name="endpoints"/>.</summary>
    /// <param name="endpoints">The app's endpoints.</param>
    /// <param name="hasFallbackPolicy">Whether a fallback authorization policy is configured.</param>
    /// <param name="allowList">The loosening allow-list.</param>
    public static SecurityGuardReport Analyze(
        IEnumerable<Endpoint> endpoints,
        bool hasFallbackPolicy,
        LooseningAllowList allowList)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(allowList);

        var entries = new List<EndpointSecurityEntry>();
        var findings = new List<SecurityGuardFinding>();

        foreach (var endpoint in endpoints.OfType<RouteEndpoint>())
        {
            var metadata = endpoint.Metadata;
            if (metadata.Any(m => m.GetType().FullName == StaticAssetDescriptorTypeName))
                continue;

            var route = LooseningAllowList.Normalize(endpoint.RoutePattern.RawText ?? string.Empty);
            var methods = metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? [];
            var label = methods.Count > 0 ? $"{string.Join(',', methods)} {route}" : route;

            var anonymous = metadata.GetMetadata<IAllowAnonymous>() is not null;
            var authorizeData = metadata.GetOrderedMetadata<IAuthorizeData>();
            var hasPolicyObjects = metadata.GetOrderedMetadata<AuthorizationPolicy>().Count > 0
                                   || metadata.GetOrderedMetadata<IAuthorizationRequirementData>().Count > 0;
            var policies = Policies(authorizeData, hasPolicyObjects);

            var loosening = metadata.GetMetadata<LoosenedAttribute>();
            var security = metadata.GetMetadata<EndpointSecurityPolicyAttribute>();
            var classification = security?.Classification ?? DataClassification.Unspecified;
            var network = security?.Network ?? NetworkRequirement.Any;

            EndpointAccess access;
            string? reason = null;
            string? ticket = null;
            var listed = false;

            if (anonymous)
            {
                access = EndpointAccess.Loosened;
                var entry = allowList.Find(route, methods);
                listed = entry is not null;
                reason = loosening?.Reason ?? (string.IsNullOrWhiteSpace(entry?.Reason) ? null : entry.Reason);
                ticket = loosening?.Ticket ?? entry?.Ticket;

                if (reason is null)
                {
                    findings.Add(new(SecurityGuardSeverity.Error, "anonymous-without-reason", label,
                        "Anonymous without a reason. Use .Loosen(reason) / AllowAnonymous(reason) / [Loosened(reason)], " +
                        "or give the allow-list entry a reason."));
                }
                else if (!listed && loosening?.Framework != true)
                {
                    findings.Add(new(SecurityGuardSeverity.Unlisted, "loosening-not-allow-listed", label,
                        $"Anonymous ('{reason}') but missing from the loosening allow-list."));
                }

                if (classification is DataClassification.Confidential or DataClassification.Restricted)
                {
                    findings.Add(new(SecurityGuardSeverity.Error, "anonymous-classified-data", label,
                        $"Classified {classification} but open to anonymous callers."));
                }
            }
            else if (authorizeData.Count > 0 || hasPolicyObjects)
            {
                access = EndpointAccess.Policed;
            }
            else if (hasFallbackPolicy)
            {
                access = EndpointAccess.Fallback;
            }
            else
            {
                access = EndpointAccess.Unpoliced;
                findings.Add(new(SecurityGuardSeverity.Error, "unpoliced", label,
                    "No authorization and no fallback policy. Require authorization, call .Loosen(reason), " +
                    "or set a fallback policy (AddModulusAuthorization sets one)."));
            }

            if (network != NetworkRequirement.Any)
            {
                findings.Add(new(SecurityGuardSeverity.Error, "network-not-enforced", label,
                    $"Declares network requirement {network}, which is not enforced yet."));
            }

            entries.Add(new EndpointSecurityEntry(
                route, [.. methods], endpoint.DisplayName, access, policies, reason, ticket, listed, classification, network));
        }

        return new SecurityGuardReport(entries, findings);
    }

    private static string[] Policies(IReadOnlyList<IAuthorizeData> authorizeData, bool hasPolicyObjects)
    {
        var policies = new List<string>();
        foreach (var data in authorizeData)
        {
            if (!string.IsNullOrEmpty(data.Policy))
                policies.Add(data.Policy);
            if (!string.IsNullOrEmpty(data.Roles))
                policies.Add("roles:" + data.Roles);
            if (string.IsNullOrEmpty(data.Policy) && string.IsNullOrEmpty(data.Roles))
                policies.Add("(authenticated)");
        }

        if (hasPolicyObjects)
            policies.Add("(policy object)");

        return [.. policies.Distinct(StringComparer.Ordinal)];
    }
}
