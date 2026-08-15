using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Settings.Domain.Constants;

public static class Schemas
{
    public const string Settings = "settings";
}

public static class SettingTypes
{
    public const string String = "string";
    public const string Integer = "integer";
    public const string Decimal = "decimal";
    public const string Boolean = "boolean";
    public const string Json = "json";
    public const string DateTime = "datetime";
}

public static class SettingScopes
{
    public const string System = "system";
    public const string Tenant = "tenant";
    public const string User = "user";
    public const string Module = "module";
}

public static class SettingErrors
{
    public static readonly Error NotFound = Error.NotFound("Setting.NotFound", "Setting not found");
    public static readonly Error DuplicateKey = Error.Conflict("Setting.DuplicateKey", "A setting with this key already exists");
    public static readonly Error EmptyKey = Error.Validation("Setting.EmptyKey", "Setting key cannot be empty");
    public static readonly Error EmptyValue = Error.Validation("Setting.EmptyValue", "Setting value cannot be empty");
    public static readonly Error InvalidType = Error.Validation("Setting.InvalidType", "Invalid setting type");
    public static readonly Error InvalidScope = Error.Validation("Setting.InvalidScope", "Invalid setting scope");
    public static readonly Error TypeMismatch = Error.Validation("Setting.TypeMismatch", "Setting value does not match the specified type");
    public static readonly Error CannotDeleteSystemSetting = Error.BusinessRule("Setting.CannotDeleteSystemSetting", "Cannot delete system setting");
    public static readonly Error ReadOnlySetting = Error.BusinessRule("Setting.ReadOnlySetting", "Cannot modify read-only setting");
}