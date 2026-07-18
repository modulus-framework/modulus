namespace Modulus.Core.Abstractions.Entities;

/// <summary>
/// The sensitivity classification of a single field, declared on the model and read by
/// the field-level security layer (blueprint §5.9, §11). Higher values are more
/// sensitive. A field's classification, combined with a field security profile, decides
/// which principals may read or write it — so two users who both legitimately open the
/// same record can still be entitled to <i>different fields</i> of it.
/// </summary>
public enum FieldClassification
{
    /// <summary>Openly readable and writable by any caller who passes the upstream layers.</summary>
    Public = 0,

    /// <summary>Internal, non-sensitive detail — restricted from external/guest contexts.</summary>
    Internal = 1,

    /// <summary>Commercially or operationally sensitive (cost, margin, performance notes).</summary>
    Confidential = 2,

    /// <summary>Highly sensitive (compensation, PII/PHI, legal) — the tightest clearance.</summary>
    Restricted = 3,
}
