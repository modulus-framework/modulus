namespace ProcureFlow.Modules.TradeFinance.Domain.Rules;

/// <summary>
/// Signals used by the LC prerequisite gate (BR-LC-01, BR-IMP-01/02).
/// </summary>
public sealed record LcPrerequisiteInput(
    bool HasAcceptedPi,
    bool HasInsuranceCoverNote,
    bool HasInsuranceMoneyReceipt,
    bool HasValidIrc,
    bool IrcHasCeiling,
    bool IsLcaComplete,
    bool IsHsClassified,
    bool IsPermitCheckPassed,
    bool IsOnCfrOrFobTerms);

/// <summary>
/// Deterministic gate for the BR-LC-01 / BR-IMP-01 / BR-IMP-02 prerequisites.
/// Returns the list of unmet prerequisites (empty ⇒ pass).
/// </summary>
public static class LcPrerequisiteGate
{
    public static IReadOnlyList<string> Evaluate(LcPrerequisiteInput input)
    {
        var failures = new List<string>();

        if (!input.HasAcceptedPi)
            failures.Add("No accepted PI (BR-IMP-01)");
        if (!input.HasValidIrc || !input.IrcHasCeiling)
            failures.Add("No valid IRC with ceiling (BR-IMP-01)");
        if (!input.IsLcaComplete)
            failures.Add("LCA form data incomplete (BR-LC-01)");
        if (!input.IsHsClassified)
            failures.Add("Items are not HS-classified (BR-LC-01)");
        if (!input.IsPermitCheckPassed)
            failures.Add("Permit check failed for the file category (BR-PM-01)");
        if (input.IsOnCfrOrFobTerms && (!input.HasInsuranceCoverNote || !input.HasInsuranceMoneyReceipt))
            failures.Add("Marine cover note + money receipt required before LC on CFR/FOB terms (BR-IMP-02)");

        return failures;
    }
}