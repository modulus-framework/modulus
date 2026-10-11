namespace Modulus.Authorization.Workflow;

/// <summary>Checks a chain before it is stored, so a bad one fails when it is saved and not when a document is submitted.</summary>
public static class ApprovalChainValidator
{
    /// <summary>The problems with <paramref name="chain"/>; empty when it is fine.</summary>
    public static IReadOnlyList<string> Validate(ApprovalChain chain, int maxSteps = 20)
    {
        ArgumentNullException.ThrowIfNull(chain);
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(chain.Name))
            errors.Add("The chain needs a name.");
        if (string.IsNullOrWhiteSpace(chain.DocumentType))
            errors.Add("The chain needs a document type.");
        if (chain.MinAmount is < 0 || chain.MaxAmount is < 0)
            errors.Add("Amounts cannot be negative.");
        if (chain.MinAmount is { } min && chain.MaxAmount is { } max && min > max)
            errors.Add("The smallest amount is above the largest.");
        if (chain.Steps is not { Count: > 0 })
            errors.Add("A chain needs at least one step.");
        else if (chain.Steps.Count > maxSteps)
            errors.Add($"A chain may have at most {maxSteps} steps.");

        foreach (var (step, index) in (chain.Steps ?? []).Select((s, i) => (s, i)))
        {
            var label = $"Step {index + 1}";
            if (string.IsNullOrWhiteSpace(step.Name))
                errors.Add($"{label} needs a name.");
            if (IsUnsafe(step.Permission))
                errors.Add($"{label} needs a specific permission (not empty, '*' or an authorization permission).");
            if (step.RequiredApprovals is < 1 or > 10)
                errors.Add($"{label}: required approvals must be between 1 and 10.");
            if (step.EscalateAfter is { } after && after <= TimeSpan.Zero)
                errors.Add($"{label}: the escalation time must be positive.");
            if ((step.EscalateAfter is null) != (string.IsNullOrWhiteSpace(step.EscalationPermission)))
                errors.Add($"{label}: set both the escalation time and the escalation permission, or neither.");
            if (step.EscalationPermission is not null && IsUnsafe(step.EscalationPermission))
                errors.Add($"{label}: the escalation permission must be specific.");
        }

        return errors;
    }

    private static bool IsUnsafe(string? permission)
        => string.IsNullOrWhiteSpace(permission)
           || permission.Contains('*', StringComparison.Ordinal)
           || permission.StartsWith("authorization:", StringComparison.OrdinalIgnoreCase);
}
