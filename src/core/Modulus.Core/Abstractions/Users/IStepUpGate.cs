namespace Modulus.Core.Abstractions;

/// <summary>
/// Whether the current caller may use a permission that needs a recent multi-factor sign-in (step-up). Registered by the
/// platform when step-up is configured; without a registration no permission needs step-up.
/// </summary>
public interface IStepUpGate
{
    /// <summary>True when the caller may use <paramref name="permission"/> now (not step-up-gated, or signed in recently with a second factor).</summary>
    bool IsSatisfied(string permission);
}
