namespace Modulus.Analytics;

using Modulus.Core.Abstractions.Entities;

/// <summary>One field of an analytics dataset.</summary>
/// <param name="Name">The field name as the provider sees it: lower snake case, letters, digits and underscores.</param>
/// <param name="Type">The logical type.</param>
/// <param name="IsNullable">Whether the value may be absent.</param>
/// <param name="Classification">How sensitive the field is. It decides the default <see cref="Masking"/>.</param>
/// <param name="Masking">
/// What is done to the value before delivery. When <see langword="null"/> the default for the
/// <paramref name="Classification"/> applies (see <see cref="AnalyticsMasking.DefaultFor(FieldClassification)"/>).
/// </param>
public sealed record AnalyticsFieldDescriptor(
    string Name,
    AnalyticsFieldType Type,
    bool IsNullable = false,
    FieldClassification Classification = FieldClassification.Public,
    AnalyticsMaskingAction? Masking = null)
{
    /// <summary>The masking that actually applies: the explicit one, or the default for the classification.</summary>
    public AnalyticsMaskingAction EffectiveMasking => Masking ?? AnalyticsMasking.DefaultFor(Classification);
}
