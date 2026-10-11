namespace Modulus.Cli.Services;

/// <summary>Result of a validation operation</summary>
internal class ValidationResult
{
    /// <summary>Whether validation passed</summary>
    public bool IsValid { get; set; }

    /// <summary>List of field errors</summary>
    public List<FieldError> Errors { get; set; } = [];

    /// <summary>List of field warnings</summary>
    public List<FieldWarning> Warnings { get; set; } = [];

    /// <summary>Create a successful validation result</summary>
    public static ValidationResult Success() => new() { IsValid = true };

    /// <summary>Create a failed validation result with errors</summary>
    public static ValidationResult Failed(params FieldError[] errors) =>
        new() { IsValid = false, Errors = errors.ToList() };

    /// <summary>Create a failed validation result with message</summary>
    public static ValidationResult Failed(string message, string field = "general") =>
        new() { IsValid = false, Errors = [new FieldError { Field = field, Message = message }] };
}

/// <summary>Validation error for a specific field</summary>
internal class FieldError
{
    /// <summary>Field name that failed validation</summary>
    public string Field { get; set; } = string.Empty;

    /// <summary>Error message to display</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Error code for programmatic handling</summary>
    public string? Code { get; set; }

    /// <summary>Severity of the error (error, warning, info)</summary>
    public string Severity { get; set; } = "error";
}

/// <summary>Validation warning for a field</summary>
internal class FieldWarning
{
    /// <summary>Field name with warning</summary>
    public string Field { get; set; } = string.Empty;

    /// <summary>Warning message</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Warning code for programmatic handling</summary>
    public string? Code { get; set; }
}

/// <summary>Validation context with entity data and rules</summary>
internal class ValidationContext
{
    /// <summary>The entity being validated</summary>
    public object? Entity { get; set; }

    /// <summary>Name of the entity being validated</summary>
    public string EntityName { get; set; } = string.Empty;

    /// <summary>Property name being validated (for field-level validation)</summary>
    public string? PropertyName { get; set; }

    /// <summary>Additional context data</summary>
    public Dictionary<string, object> AdditionalData { get; set; } = [];

    /// <summary>Current culture for localization</summary>
    public System.Globalization.CultureInfo Culture { get; set; } = System.Globalization.CultureInfo.CurrentCulture;
}

/// <summary>Represents a validation rule</summary>
internal interface IValidationRule
{
    /// <summary>Get the name of this rule</summary>
    string RuleName { get; }

    /// <summary>Validate the given value</summary>
    ValidationResult Validate(object? value, ValidationContext context);

    /// <summary>Validate asynchronously</summary>
    Task<ValidationResult> ValidateAsync(object? value, ValidationContext context);
}

/// <summary>Built-in validation severity levels</summary>
internal static class ValidationSeverity
{
    public const string Error = "error";
    public const string Warning = "warning";
    public const string Info = "info";
    public const string Success = "success";
}

/// <summary>Standard validation error codes</summary>
internal static class ValidationCodes
{
    public const string Required = "REQUIRED";
    public const string InvalidFormat = "INVALID_FORMAT";
    public const string RangeError = "RANGE_ERROR";
    public const string LengthError = "LENGTH_ERROR";
    public const string DuplicateValue = "DUPLICATE_VALUE";
    public const string CrossFieldError = "CROSS_FIELD_ERROR";
    public const string CustomError = "CUSTOM_ERROR";
    public const string AsyncError = "ASYNC_ERROR";
}
