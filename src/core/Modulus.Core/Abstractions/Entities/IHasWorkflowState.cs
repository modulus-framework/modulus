namespace Modulus.Core.Abstractions.Entities;

/// <summary>
/// Implement on a business document that moves through a lifecycle
/// (Draft → Submitted → Approved → Posted → Closed → Archived, or
/// Rejected/Cancelled). Workflow-aware authorization reads the current
/// <see cref="WorkflowState"/> as an <b>attribute</b> and lets policies condition
/// actions on it ("<c>edit</c> allowed only while Draft or Rejected"; "only an
/// Approved document may be Posted") — blueprint §5.8.
/// <para>
/// The framework is workflow-<i>aware</i>, not a workflow <i>engine</i>: the state is
/// owned and transitioned by the domain/workflow module; authorization merely consumes
/// it. It is a free-form string so the framework stays decoupled from any particular
/// state enum; policies compare it case-insensitively.
/// </para>
/// </summary>
public interface IHasWorkflowState
{
    /// <summary>The record's current lifecycle state (domain-defined).</summary>
    string WorkflowState { get; }
}
