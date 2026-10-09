namespace Modulus.Authorization.Approval;

using System.Collections.Concurrent;
using Modulus.Authorization.Governance;
using Modulus.Authorization.Grants;
using Modulus.Authorization.Organization;
using Modulus.Authorization.Scopes;
using Modulus.Core.Abstractions.Entities;

/// <summary>
/// "Up to this amount": a holder (role or user) may use a permission on documents of a value up to <see cref="MaxAmount"/>.
/// The permission says <i>what</i> the holder may do; this says <i>how much</i>. Configurable data, never role names in code.
/// </summary>
/// <param name="HolderType">Whether the holder is a role or a user.</param>
/// <param name="Holder">The role name, or the user id.</param>
/// <param name="Permission">The permission the limit applies to (exact, e.g. <c>procurement:purchase-order:approve</c>).</param>
/// <param name="MaxAmount">The largest document value the holder may act on.</param>
/// <param name="Currency">The currency the limit is in; null applies to documents without a currency. A document in another currency is not covered.</param>
/// <param name="DocumentType">The document type (the resource's type name); null covers every type.</param>
/// <param name="OrgUnitId">Limits the authority to documents of this org unit and its descendants; null for any.</param>
/// <param name="ValidFrom">Effective from this instant; null from the start.</param>
/// <param name="ValidUntil">Effective until this instant (exclusive); null for good.</param>
public sealed record ApprovalAuthority(
    GrantHolderType HolderType,
    string Holder,
    string Permission,
    decimal MaxAmount,
    string? Currency = null,
    string? DocumentType = null,
    Guid? OrgUnitId = null,
    DateTimeOffset? ValidFrom = null,
    DateTimeOffset? ValidUntil = null)
{
    /// <summary>Whether the authority counts at <paramref name="now"/> (decided per decision, like grants).</summary>
    public bool IsValidAt(DateTimeOffset now)
        => (ValidFrom is null || now >= ValidFrom) && (ValidUntil is null || now < ValidUntil);
}

/// <summary>What a document looks like to an approval-authority check.</summary>
/// <param name="Amount">The document value.</param>
/// <param name="Currency">Its currency, or null.</param>
/// <param name="DocumentType">The document type (the resource's type name).</param>
/// <param name="OrgUnitId">The org unit that owns the document, or null.</param>
public sealed record ApprovalContext(decimal Amount, string? Currency, string? DocumentType, Guid? OrgUnitId)
{
    /// <summary>Reads the context from a document; null when it carries no <see cref="IHasApprovalAmount"/>.</summary>
    public static ApprovalContext? From(object resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        return resource is IHasApprovalAmount amount
            ? new ApprovalContext(amount.ApprovalAmount, amount.ApprovalCurrency, resource.GetType().Name, (resource as IHasOrgUnit)?.OrgUnitId)
            : null;
    }
}

/// <summary>The answer of an approval-authority check.</summary>
/// <param name="IsWithinAuthority">Whether the principal may act on a document of this value.</param>
/// <param name="ReasonCode">An <see cref="Resources.AccessReasonCodes"/> value: allowed, no authority, or limit exceeded.</param>
/// <param name="Limit">The largest limit that applies to the document (the one compared), or null when none applies.</param>
public sealed record ApprovalCheck(bool IsWithinAuthority, string ReasonCode, decimal? Limit);

/// <summary>Where approval limits are kept.</summary>
public interface IApprovalAuthorityStore
{
    /// <summary>The authorities for <paramref name="permission"/> held directly by the user or through any of the principal's roles.</summary>
    IReadOnlyCollection<ApprovalAuthority> GetAuthorities(PrincipalGrantQuery principal, string permission);
}

/// <summary>The default store: in memory, empty until limits are added (so nobody has authority).</summary>
public sealed class InMemoryApprovalAuthorityStore : IApprovalAuthorityStore
{
    private readonly ConcurrentBag<ApprovalAuthority> _authorities = [];

    /// <summary>Adds a limit.</summary>
    public InMemoryApprovalAuthorityStore Add(ApprovalAuthority authority)
    {
        ArgumentNullException.ThrowIfNull(authority);
        _authorities.Add(authority);
        return this;
    }

    /// <inheritdoc />
    public IReadOnlyCollection<ApprovalAuthority> GetAuthorities(PrincipalGrantQuery principal, string permission)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return [.. _authorities.Where(a =>
            string.Equals(a.Permission, permission, StringComparison.OrdinalIgnoreCase)
            && ApprovalAuthorityEvaluator.HeldBy(a, principal))];
    }
}

/// <summary>Decides whether the principal's approval limits cover a document.</summary>
public interface IApprovalAuthorityEvaluator
{
    /// <summary>Checks the current principal against the limits for <paramref name="permission"/>.</summary>
    ApprovalCheck Check(string permission, object resource);

    /// <summary>Checks an arbitrary principal (reports, what-if).</summary>
    ApprovalCheck Check(PrincipalGrantQuery principal, string permission, ApprovalContext context);
}

/// <summary>
/// Default <see cref="IApprovalAuthorityEvaluator"/>. Rules: a limit applies when its permission, document type, currency, org unit
/// and validity window match; the <b>largest</b> applicable limit is compared with the amount; no applicable limit, a document without
/// an amount, or an amount over the limit is refused (fail closed). A delegate may use the delegator's limits, never more.
/// </summary>
public sealed class ApprovalAuthorityEvaluator(
    IApprovalAuthorityStore store,
    IPrincipalGrantQuerySource principal,
    IOrgHierarchy? hierarchy = null,
    IDelegationStore? delegations = null,
    TimeProvider? clock = null) : IApprovalAuthorityEvaluator
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    internal static bool HeldBy(ApprovalAuthority authority, PrincipalGrantQuery who)
        => authority.HolderType is GrantHolderType.Role
            ? who.Roles.Contains(authority.Holder, StringComparer.OrdinalIgnoreCase)
            : who.UserId is { } id && string.Equals(authority.Holder, id.ToString(), StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public ApprovalCheck Check(string permission, object resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        var context = ApprovalContext.From(resource);
        return context is null
            ? new ApprovalCheck(false, Resources.AccessReasonCodes.MetadataMissing, null)
            : Check(principal.Current, permission, context);
    }

    /// <inheritdoc />
    public ApprovalCheck Check(PrincipalGrantQuery who, string permission, ApprovalContext context)
    {
        ArgumentNullException.ThrowIfNull(who);
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);
        ArgumentNullException.ThrowIfNull(context);

        var now = _clock.GetUtcNow();
        var limit = LargestLimit(who, permission, context, now);
        if (who.UserId is { } userId && delegations is not null)
        {
            foreach (var delegation in delegations.ActiveFor(userId, now))
            {
                if (!delegation.Permissions.Contains(permission))
                    continue;

                var lent = LargestLimit(new PrincipalGrantQuery(delegation.FromUserId, delegation.FromRoles), permission, context, now);
                if (lent is { } value && (limit is null || value > limit))
                    limit = value;
            }
        }

        if (limit is null)
            return new ApprovalCheck(false, Resources.AccessReasonCodes.NoApprovalAuthority, null);
        return context.Amount <= limit
            ? new ApprovalCheck(true, Resources.AccessReasonCodes.Allowed, limit)
            : new ApprovalCheck(false, Resources.AccessReasonCodes.ApprovalLimitExceeded, limit);
    }

    private decimal? LargestLimit(PrincipalGrantQuery who, string permission, ApprovalContext context, DateTimeOffset now)
    {
        decimal? best = null;
        foreach (var authority in store.GetAuthorities(who, permission))
        {
            if (!authority.IsValidAt(now) || !Matches(authority, context))
                continue;
            if (best is null || authority.MaxAmount > best)
                best = authority.MaxAmount;
        }

        return best;
    }

    private bool Matches(ApprovalAuthority authority, ApprovalContext context)
    {
        if (authority.DocumentType is { } type && !string.Equals(type, context.DocumentType, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.Equals(authority.Currency, context.Currency, StringComparison.OrdinalIgnoreCase))
            return false;
        if (authority.OrgUnitId is not { } unit)
            return true;

        return context.OrgUnitId is { } documentUnit
               && (documentUnit == unit || (hierarchy?.Descendants(unit).Contains(documentUnit) ?? false));
    }
}
