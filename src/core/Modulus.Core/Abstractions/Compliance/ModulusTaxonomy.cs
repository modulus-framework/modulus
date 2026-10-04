namespace Modulus.Core.Abstractions.Compliance;

using Microsoft.Extensions.Compliance.Classification;
using SecurityLevel = Modulus.Core.Abstractions.Security.DataClassification;

/// <summary>
/// The Modulus data-classification taxonomy, shared by endpoints, entities, DTOs and log calls. Two axes, because they
/// call for different treatment in logs:
/// <list type="bullet">
/// <item>the sensitivity levels <see cref="Internal"/>, <see cref="Confidential"/> and <see cref="Restricted"/> (the same words
/// as the endpoint classification <see cref="SecurityLevel"/>);</item>
/// <item><see cref="Personal"/> (identifies a person: pseudonymized, so log lines about one person still correlate) and
/// <see cref="Secret"/> (credentials: never written, whatever the settings).</item>
/// </list>
/// Annotate <c>[LoggerMessage]</c> parameters and <c>[LogProperties]</c> members with the matching attribute
/// (<see cref="InternalDataAttribute"/>, <see cref="ConfidentialDataAttribute"/>, <see cref="RestrictedDataAttribute"/>,
/// <see cref="PersonalInformationAttribute"/>, <see cref="SecretDataAttribute"/>); <c>AddModulusRedaction</c> maps each one to a
/// redactor.
/// </summary>
public static class ModulusTaxonomy
{
    /// <summary>The taxonomy name.</summary>
    public const string Name = "Modulus";

    /// <summary>Company-internal (ids, slugs, codes). Not redacted: logs are internal.</summary>
    public static DataClassification Internal { get; } = new(Name, nameof(Internal));

    /// <summary>Business-sensitive (prices, contracts, aggregated salaries). Erased.</summary>
    public static DataClassification Confidential { get; } = new(Name, nameof(Confidential));

    /// <summary>The most sensitive data (payroll, health, bank details). Erased.</summary>
    public static DataClassification Restricted { get; } = new(Name, nameof(Restricted));

    /// <summary>Identifies a person (name, e-mail address, phone, postal address). Pseudonymized with an HMAC, else erased.</summary>
    public static DataClassification Personal { get; } = new(Name, nameof(Personal));

    /// <summary>Passwords, tokens, keys, connection strings. Always erased, even with redaction switched off.</summary>
    public static DataClassification Secret { get; } = new(Name, nameof(Secret));

    /// <summary>Every classification of the taxonomy.</summary>
    public static IReadOnlyList<DataClassification> All { get; } = [Internal, Confidential, Restricted, Personal, Secret];

    /// <summary>
    /// The log classification of an endpoint classification level: <see cref="SecurityLevel.Public"/> is
    /// <see cref="DataClassification.None"/>, <see cref="SecurityLevel.Unspecified"/> is <see cref="DataClassification.Unknown"/>
    /// (redacted by the fallback, i.e. erased).
    /// </summary>
    public static DataClassification For(SecurityLevel level) => level switch
    {
        SecurityLevel.Public => DataClassification.None,
        SecurityLevel.Internal => Internal,
        SecurityLevel.Confidential => Confidential,
        SecurityLevel.Restricted => Restricted,
        _ => DataClassification.Unknown,
    };
}

/// <summary>Company-internal data (<see cref="ModulusTaxonomy.Internal"/>): not redacted.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.ReturnValue, AllowMultiple = true)]
public sealed class InternalDataAttribute() : DataClassificationAttribute(ModulusTaxonomy.Internal);

/// <summary>Business-sensitive data (<see cref="ModulusTaxonomy.Confidential"/>): erased from logs.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.ReturnValue, AllowMultiple = true)]
public sealed class ConfidentialDataAttribute() : DataClassificationAttribute(ModulusTaxonomy.Confidential);

/// <summary>The most sensitive data (<see cref="ModulusTaxonomy.Restricted"/>): erased from logs.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.ReturnValue, AllowMultiple = true)]
public sealed class RestrictedDataAttribute() : DataClassificationAttribute(ModulusTaxonomy.Restricted);

/// <summary>
/// Identifies a person (<see cref="ModulusTaxonomy.Personal"/>): pseudonymized in logs. Not named <c>PersonalData</c>, which
/// ASP.NET Core Identity already uses for its download/delete feature.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.ReturnValue, AllowMultiple = true)]
public sealed class PersonalInformationAttribute() : DataClassificationAttribute(ModulusTaxonomy.Personal);

/// <summary>Credentials (<see cref="ModulusTaxonomy.Secret"/>): never written to a log.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.ReturnValue, AllowMultiple = true)]
public sealed class SecretDataAttribute() : DataClassificationAttribute(ModulusTaxonomy.Secret);
