using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Configuration.Domain.Constants;

public static class Schemas
{
    public const string Features = "features";
}

public static class FeatureErrors
{
    public static readonly Error NotFound = Error.NotFound("Feature.NotFound", "Feature flag not found");
    public static readonly Error DuplicateKey = Error.Conflict("Feature.DuplicateKey", "A feature flag with this key already exists");
    public static readonly Error EmptyName = Error.Validation("Feature.EmptyName", "Name cannot be empty");
    public static readonly Error NameTooLong = Error.Validation("Feature.NameTooLong", "Name cannot exceed 200 characters");
    public static readonly Error DescriptionTooLong = Error.Validation("Feature.DescriptionTooLong", "Description cannot exceed 500 characters");
}
