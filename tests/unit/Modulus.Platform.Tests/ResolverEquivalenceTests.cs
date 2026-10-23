using FluentAssertions;
using Modulus.Authorization;
using Modulus.Authorization.Grants;
using Modulus.Core.Abstractions;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>
/// The resolver's grant pass starts the implication closure from the allowed permissions that name prerequisites. Its result must be the
/// same as a full closure over every allowed permission, which is what <see cref="Reference"/> computes. Random registries and grant sets
/// (wildcards, denials, restrictions, lapsed and future grants, retired permissions, prerequisite cycles) must agree.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ResolverEquivalenceTests
{
    private static readonly IReadOnlySet<string> Empty = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    // The algorithm before the change, kept as the specification: every allowed permission is a starting point of the closure.
    private static IReadOnlySet<string> Reference(IPermissionRegistry registry, PrincipalGrantQuery principal, IReadOnlyCollection<PermissionGrant> grants, DateTimeOffset now)
    {
        if (grants.Count == 0)
            return Empty;
        var definitions = registry.GetAll();
        var requires = definitions.ToDictionary(d => d.Permission, d => d.Requires, StringComparer.OrdinalIgnoreCase);
        var retired = new HashSet<string>(definitions.Where(d => d.Lifecycle is PermissionLifecycle.Retired).Select(d => d.Permission), StringComparer.OrdinalIgnoreCase);
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var denied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var grant in grants)
        {
            if (!grant.IsValidAt(now) || grant.Type is PermissionGrantType.Restrict)
                continue;
            var target = grant.Type is PermissionGrantType.Allow ? allowed : denied;
            if (grant.Permission.EndsWith(":*", StringComparison.Ordinal))
            {
                var prefix = grant.Permission[..^1];
                foreach (var definition in definitions)
                {
                    if (grant.Type is not PermissionGrantType.Deny && definition.Sensitivity is PermissionSensitivity.Critical)
                        continue;
                    if (definition.Permission.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        target.Add(definition.Permission);
                }
            }
            else
            {
                target.Add(grant.Permission);
            }
        }

        var pending = new Stack<string>(allowed);
        while (pending.Count > 0)
        {
            var permission = pending.Pop();
            if (retired.Contains(permission) || !requires.TryGetValue(permission, out var needed))
                continue;
            foreach (var required in needed)
                if (allowed.Add(required))
                    pending.Push(required);
        }

        if (denied.Count > 0)
            allowed.ExceptWith(denied);
        allowed.ExceptWith(retired);
        return allowed;
    }

    private static PermissionRegistry RandomRegistry(Random random)
    {
        var registry = new PermissionRegistry();
        var names = Enumerable.Range(0, 60).Select(i => $"m{i % 4}:r{i % 7}:act{i}").ToList();
        foreach (var name in names)
        {
            var requires = Enumerable.Range(0, random.Next(0, 3)).Select(_ => names[random.Next(names.Count)]).Where(r => r != name).Distinct().ToArray();
            var sensitivity = random.Next(5) == 0 ? PermissionSensitivity.Critical : PermissionSensitivity.Normal;
            var lifecycle = random.Next(10) == 0 ? PermissionLifecycle.Retired : PermissionLifecycle.Active;
            registry.Add(name, "Random permission.", requires, sensitivity, lifecycle, replacedBy: null);
        }
        registry.Freeze();
        return registry;
    }

    private static IReadOnlyCollection<PermissionGrant> RandomGrants(Random random, DateTimeOffset now)
    {
        var grants = new List<PermissionGrant>();
        for (var i = 0; i < random.Next(1, 25); i++)
        {
            var permission = random.Next(6) == 0
                ? $"m{random.Next(4)}:r{random.Next(7)}:*"
                : $"m{random.Next(4)}:r{random.Next(7)}:act{random.Next(60)}";
            var type = (PermissionGrantType)random.Next(3);
            DateTimeOffset? validFrom = random.Next(8) == 0 ? now.AddDays(1) : null;
            DateTimeOffset? validUntil = random.Next(8) == 0 ? now.AddDays(-1) : null;
            grants.Add(new PermissionGrant(GrantHolderType.Role, "Role", permission, type, null, validFrom, validUntil));
        }
        return grants;
    }

    [Fact]
    public void The_grant_pass_gives_the_same_permissions_as_a_full_closure_on_random_data()
    {
        var random = new Random(20261023);
        var now = new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
        var clock = new FixedClock(now);

        for (var round = 0; round < 400; round++)
        {
            var registry = RandomRegistry(random);
            var grants = RandomGrants(random, now);
            var principal = new PrincipalGrantQuery(null, []);
            var resolver = new PermissionResolver(new InMemoryPermissionGrantStore(), registry, clock);

            var actual = resolver.Resolve(principal, grants);
            var expected = Reference(registry, principal, grants, now);

            actual.Should().BeEquivalentTo(expected, because: $"round {round} must resolve the same permissions");
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
