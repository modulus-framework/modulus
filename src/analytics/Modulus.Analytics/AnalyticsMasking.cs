namespace Modulus.Analytics;

using Modulus.Core.Abstractions.DataProtection;
using Modulus.Core.Abstractions.Entities;

/// <summary>
/// Applies a dataset's masking policy to a record. Analytics output is batch output to a system the application does not control,
/// so the policy is fixed by the dataset's metadata and not by who triggered the export.
/// </summary>
public static class AnalyticsMasking
{
    /// <summary>
    /// The masking a classification gets unless a field says otherwise: public and internal values are kept, confidential values are
    /// hashed, and restricted values are dropped.
    /// </summary>
    /// <param name="classification">The field's classification.</param>
    public static AnalyticsMaskingAction DefaultFor(FieldClassification classification) => classification switch
    {
        FieldClassification.Public or FieldClassification.Internal => AnalyticsMaskingAction.Keep,
        FieldClassification.Confidential => AnalyticsMaskingAction.Hash,
        _ => AnalyticsMaskingAction.Drop,
    };

    /// <summary>
    /// Returns the values of <paramref name="record"/> after masking: fields to drop are removed, fields to hash are replaced by
    /// <see cref="IPersonalDataProtector.Hash(string)"/> of their text, and fields the dataset does not declare are removed.
    /// </summary>
    /// <param name="dataset">The dataset the record belongs to.</param>
    /// <param name="record">The record to mask. It is not changed.</param>
    /// <param name="protector">Supplies the keyed hash.</param>
    public static AnalyticsRecord Apply(AnalyticsDatasetDescriptor dataset, AnalyticsRecord record, IPersonalDataProtector protector)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(protector);

        var masked = new Dictionary<string, object?>(record.Values.Count, StringComparer.Ordinal);
        foreach (var field in dataset.Fields)
        {
            if (!record.Values.TryGetValue(field.Name, out var value))
            {
                continue;
            }

            switch (field.EffectiveMasking)
            {
                case AnalyticsMaskingAction.Drop:
                    break;
                case AnalyticsMaskingAction.Hash:
                    masked[field.Name] = value is null ? null : protector.Hash(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);
                    break;
                default:
                    masked[field.Name] = value;
                    break;
            }
        }

        return record with { Values = masked };
    }
}
