using Modulus.Authorization.Governance;
using Modulus.Authorization.Grants;
using Modulus.Authorization.Scopes;

namespace Modulus.Testing.Authorization;

/// <summary>
/// The permission truth table (TEST-1): the rules every grant store and resolver must keep, each as one named case. The subject must
/// register the permissions <see cref="Read"/>, <see cref="Approve"/> and the wildcard <c>orders:*</c> expands over.
/// </summary>
public static class AuthorizationConformance
{
    /// <summary>The permissions the cases name; the subject registers them.</summary>
    public const string Read = "orders:read";
    public const string Approve = "orders:approve";

    private const string ApproverRole = "Approver";
    private const string AgentRole = "integration:tablet";

    private static readonly Guid s_person = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid s_manager = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private delegate Task<string?> Check(IAuthorizationConformanceSubject subject, IAuthorizationConformanceSubject otherCompany, CancellationToken ct);

    private sealed record Case(string Name, Check Run);

    private static readonly Case[] s_cases =
    [
        new("a direct allow grants the permission", async (s, _, ct) =>
        {
            await s.ResetAsync(ct);
            await s.AddGrantAsync(User(PermissionGrantType.Allow, Approve), ct);
            return Expect(Person(s).Contains(Approve), "the person's direct allow did not grant the permission");
        }),
        new("no grant denies: the resolver fails closed", async (s, _, ct) =>
        {
            await s.ResetAsync(ct);
            return Expect(Person(s).Count == 0, "a person with no grants resolved some permission");
        }),
        new("a role allow reaches its holders", async (s, _, ct) =>
        {
            await s.ResetAsync(ct);
            await s.AddGrantAsync(Role(PermissionGrantType.Allow, Approve), ct);
            return Expect(Person(s).Contains(Approve), "a holder of the role did not get the role's allow");
        }),
        new("a person deny beats a role allow", async (s, _, ct) =>
        {
            await s.ResetAsync(ct);
            await s.AddGrantAsync(Role(PermissionGrantType.Allow, Approve), ct);
            await s.AddGrantAsync(User(PermissionGrantType.Deny, Approve), ct);
            return Expect(!Person(s).Contains(Approve), "the person's deny did not stand over the role's allow");
        }),
        new("a role deny beats a person allow", async (s, _, ct) =>
        {
            await s.ResetAsync(ct);
            await s.AddGrantAsync(User(PermissionGrantType.Allow, Approve), ct);
            await s.AddGrantAsync(Role(PermissionGrantType.Deny, Approve), ct);
            return Expect(!Person(s).Contains(Approve), "the role's deny did not stand over the person's allow");
        }),
        new("a wildcard deny removes the permissions it names", async (s, _, ct) =>
        {
            await s.ResetAsync(ct);
            await s.AddGrantAsync(Role(PermissionGrantType.Allow, Approve), ct);
            await s.AddGrantAsync(User(PermissionGrantType.Deny, "orders:*"), ct);
            return Expect(!Person(s).Contains(Approve), "a wildcard deny did not remove a named allow");
        }),
        new("an expired grant grants nothing", async (s, _, ct) =>
        {
            await s.ResetAsync(ct);
            await s.AddGrantAsync(User(PermissionGrantType.Allow, Approve, validUntil: DateTimeOffset.UtcNow.AddHours(-1)), ct);
            return Expect(!Person(s).Contains(Approve), "an expired grant still granted the permission");
        }),
        new("a grant that is not yet valid grants nothing", async (s, _, ct) =>
        {
            await s.ResetAsync(ct);
            await s.AddGrantAsync(User(PermissionGrantType.Allow, Approve, validFrom: DateTimeOffset.UtcNow.AddHours(1)), ct);
            return Expect(!Person(s).Contains(Approve), "a grant before its start granted the permission");
        }),
        new("a restriction alone grants nothing", async (s, _, ct) =>
        {
            await s.ResetAsync(ct);
            // A restriction must narrow a scope: a store refuses one that names the whole company, so this one narrows to the caller's own records.
            await s.AddGrantAsync(User(PermissionGrantType.Restrict, Approve, scope: PermissionScope.Own), ct);
            return Expect(Person(s).Count == 0, "a restriction on its own granted a permission");
        }),
        new("a delegated permission reaches the delegate", async (s, _, ct) =>
        {
            await s.ResetAsync(ct);
            var resolver = Delegated(s, Approve);
            return Expect(resolver.Resolve(new PrincipalGrantQuery(s_person, [])).Contains(Approve),
                "a permission delegated to the person did not reach them");
        }),
        new("a person deny stands over a delegation", async (s, _, ct) =>
        {
            await s.ResetAsync(ct);
            await s.AddGrantAsync(User(PermissionGrantType.Deny, Approve), ct);
            var resolver = Delegated(s, Approve);
            return Expect(!resolver.Resolve(new PrincipalGrantQuery(s_person, [])).Contains(Approve),
                "a delegation overrode the person's own deny");
        }),
        new("an agent cannot widen a person who lacks the permission", async (s, _, ct) =>
        {
            await s.ResetAsync(ct);
            await s.AddGrantAsync(Role(PermissionGrantType.Allow, Approve, AgentRole), ct);
            return Expect(Agent(s).Count == 0, "an agent granted the permission gave it to a person who lacks it");
        }),
        new("an agent narrows a person to its own grants", async (s, _, ct) =>
        {
            await s.ResetAsync(ct);
            await s.AddGrantAsync(User(PermissionGrantType.Allow, Approve), ct);
            await s.AddGrantAsync(User(PermissionGrantType.Allow, Read), ct);
            await s.AddGrantAsync(Role(PermissionGrantType.Allow, Approve, AgentRole), ct);
            var acting = Agent(s);
            return Expect(acting.Contains(Approve) && !acting.Contains(Read),
                "an agent did not narrow the person to the permissions it holds");
        }),
        new("without an agent the person keeps the full grant (control for the agent case)", async (s, _, ct) =>
        {
            await s.ResetAsync(ct);
            await s.AddGrantAsync(User(PermissionGrantType.Allow, Approve), ct);
            await s.AddGrantAsync(User(PermissionGrantType.Allow, Read), ct);
            var both = Person(s);
            return Expect(both.Contains(Approve) && both.Contains(Read), "the person lost a grant that no agent was involved in");
        }),
        new("a grant in one company is invisible to another", async (s, other, ct) =>
        {
            await s.ResetAsync(ct);
            await other.ResetAsync(ct);
            await s.AddGrantAsync(User(PermissionGrantType.Allow, Approve), ct);
            return Expect(Person(other).Count == 0, "a grant made in one company resolved in another");
        }),
    ];

    /// <summary>Runs every case against <paramref name="subject"/>; <paramref name="otherCompany"/> is a second, empty company for the isolation case.</summary>
    public static async Task<AuthorizationConformanceReport> RunAsync(
        IAuthorizationConformanceSubject subject,
        IAuthorizationConformanceSubject otherCompany,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(otherCompany);

        var results = new List<AuthorizationConformanceResult>(s_cases.Length);
        foreach (var conformanceCase in s_cases)
        {
            string? problem;
            try
            {
                problem = await conformanceCase.Run(subject, otherCompany, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // A case that throws is a failure: a subject must not pass by crashing.
                problem = $"threw {ex.GetType().Name}: {ex.Message}";
            }

            results.Add(new AuthorizationConformanceResult(conformanceCase.Name, problem));
        }

        return new AuthorizationConformanceReport(results);
    }

    private static string? Expect(bool holds, string problem) => holds ? null : problem;

    private static IReadOnlySet<string> Person(IAuthorizationConformanceSubject subject)
        => subject.Resolver.Resolve(new PrincipalGrantQuery(s_person, [ApproverRole]));

    private static IReadOnlySet<string> Agent(IAuthorizationConformanceSubject subject)
        => subject.Resolver.Resolve(new PrincipalGrantQuery(s_person, [ApproverRole], AgentRole));

    private static DelegationAwarePermissionResolver Delegated(IAuthorizationConformanceSubject subject, string permission)
        => new(subject.Resolver, new StubDelegations(permission));

    private static PermissionGrant User(
        PermissionGrantType type, string permission, DateTimeOffset? validFrom = null, DateTimeOffset? validUntil = null,
        PermissionScope? scope = null)
        => new(GrantHolderType.User, s_person.ToString(), permission, type, Scope: scope, ValidFrom: validFrom, ValidUntil: validUntil);

    private static PermissionGrant Role(PermissionGrantType type, string permission, string? role = null)
        => new(GrantHolderType.Role, role ?? ApproverRole, permission, type);

    private sealed class StubDelegations(string permission) : IDelegationResolver
    {
        public IReadOnlyCollection<DelegatedPermission> DelegatedPermissions(Guid delegateUserId)
            => delegateUserId == s_person ? [new DelegatedPermission(permission, s_manager, Guid.NewGuid())] : [];
    }
}
