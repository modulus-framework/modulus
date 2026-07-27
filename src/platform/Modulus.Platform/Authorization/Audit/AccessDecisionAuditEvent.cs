namespace Modulus.Authorization.Audit;

using Modulus.Events.Abstractions;

/// <summary>
/// A durable record of an allow/deny access decision (auth blueprint §5.14/§16:
/// "the deciding factor"). Unlike <see cref="AuthorizationAdministrativeChangeEvent"/>
/// (always emitted for every administrative change), this is emitted only for
/// resource types/actions declaratively marked audit-worthy via
/// <c>AddScopedDecisionAuditing</c> — auditing every decision is "prohibitively
/// voluminous" per the blueprint, so this layer is opt-in, not unconditional.
/// </summary>
/// <param name="ResourceType">The decided-upon resource's CLR type name.</param>
/// <param name="Action">
/// The action evaluated — a <see cref="Modulus.Authorization.Resources.IResourceAuthorizer"/>
/// action name, or <c>"FieldWrite:{fields}"</c> for an
/// <see cref="Modulus.Authorization.Fields.IFieldAuthorizer"/> write decision.
/// </param>
/// <param name="IsAllowed">Whether the decision allowed or denied the action.</param>
/// <param name="Reason">
/// The deciding factor — the policy/rule's denial reason, or <see langword="null"/>
/// on allow.
/// </param>
/// <param name="ActorUserId">Who the decision was evaluated for.</param>
[IntegrationEventName("authorization.access-decision.v1")]
public sealed record AccessDecisionAuditEvent(
    string ResourceType,
    string Action,
    bool IsAllowed,
    string? Reason,
    string? ActorUserId)
    : IntegrationEventBase("authorization.access-decision.v1");
