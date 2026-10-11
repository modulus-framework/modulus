using System.Reflection;

namespace Modulus.Cli.Services;

/// <summary>
/// Service for form validation supporting both sync and async validation.
/// Provides built-in validators and extensibility for custom rules.
/// </summary>
internal class FormValidationService
{
    private readonly List<IValidationRule> _rules = [];
    private readonly Dictionary<string, string> _errorMessages = [];

    /// <summary>Register a custom validation rule</summary>
    public void RegisterRule(IValidationRule rule)
    {
        _rules.Add(rule);
    }

    /// <summary>Set a custom error message for a validation code</summary>
    public void SetErrorMessage(string code, string message)
    {
        _errorMessages[code] = message;
    }

    /// <summary>Validate an entity using registered rules</summary>
    public ValidationResult Validate(object? entity, string entityName)
    {
        var context = new ValidationContext
        {
            Entity = entity,
            EntityName = entityName
        };

        var errors = new List<FieldError>();

        if (entity == null)
        {
            return ValidationResult.Failed("Entity cannot be null");
        }

        // Validate all properties
        var properties = entity.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
        foreach (var property in properties)
        {
            var value = property.GetValue(entity);
            context.PropertyName = property.Name;

            // Check required attribute
            if (!property.PropertyType.IsValueType && value == null)
            {
                var required = property.GetCustomAttribute<RequiredAttribute>();
                if (required != null)
                {
                    errors.Add(new FieldError
                    {
                        Field = property.Name,
                        Message = GetErrorMessage("REQUIRED", $"{property.Name} is required"),
                        Code = ValidationCodes.Required
                    });
                }
            }

            // Check string length
            if (value is string strValue)
            {
                var maxLength = property.GetCustomAttribute<StringLengthAttribute>();
                if (maxLength != null && strValue.Length > maxLength.MaximumLength)
                {
                    errors.Add(new FieldError
                    {
                        Field = property.Name,
                        Message = GetErrorMessage("LENGTH_ERROR",
                            $"{property.Name} must be at most {maxLength.MaximumLength} characters"),
                        Code = ValidationCodes.LengthError
                    });
                }

                // Check minimum length
                if (maxLength?.MinimumLength > 0 && strValue.Length < maxLength.MinimumLength)
                {
                    errors.Add(new FieldError
                    {
                        Field = property.Name,
                        Message = GetErrorMessage("LENGTH_ERROR",
                            $"{property.Name} must be at least {maxLength.MinimumLength} characters"),
                        Code = ValidationCodes.LengthError
                    });
                }
            }

            // Check range for numeric values
            if (value is IComparable)
            {
                var range = property.GetCustomAttribute<RangeAttribute>();
                if (range != null)
                {
                    if (value is decimal d)
                    {
                        var min = Convert.ToDecimal(range.Minimum);
                        var max = Convert.ToDecimal(range.Maximum);
                        if (d < min || d > max)
                        {
                            errors.Add(new FieldError
                            {
                                Field = property.Name,
                                Message = GetErrorMessage("RANGE_ERROR",
                                    $"{property.Name} must be between {range.Minimum} and {range.Maximum}"),
                                Code = ValidationCodes.RangeError
                            });
                        }
                    }
                }
            }

            // Check email format
            if (value is string email)
            {
                var emailAttr = property.GetCustomAttribute<EmailAddressAttribute>();
                if (emailAttr != null && !IsValidEmail(email))
                {
                    errors.Add(new FieldError
                    {
                        Field = property.Name,
                        Message = GetErrorMessage("INVALID_FORMAT", $"{property.Name} must be a valid email address"),
                        Code = ValidationCodes.InvalidFormat
                    });
                }
            }
        }

        return new ValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors
        };
    }

    /// <summary>Validate an entity asynchronously</summary>
    public async Task<ValidationResult> ValidateAsync(object? entity, string entityName)
    {
        // Start with sync validation
        var result = Validate(entity, entityName);

        if (!result.IsValid)
            return result;

        // Run async rules
        var context = new ValidationContext
        {
            Entity = entity,
            EntityName = entityName
        };

        foreach (var rule in _rules)
        {
            var ruleResult = await rule.ValidateAsync(entity, context);
            if (!ruleResult.IsValid)
            {
                result.IsValid = false;
                result.Errors.AddRange(ruleResult.Errors);
            }
        }

        return result;
    }

    /// <summary>Validate a specific property value</summary>
    public ValidationResult ValidateProperty(object? entity, string propertyName)
    {
        if (entity == null)
            return ValidationResult.Failed("Entity cannot be null");

        var property = entity.GetType().GetProperty(propertyName);
        if (property == null)
            return ValidationResult.Failed($"Property {propertyName} not found");

        var value = property.GetValue(entity);
        var errors = new List<FieldError>();

        // Check required
        if (value == null && !property.PropertyType.IsValueType)
        {
            var required = property.GetCustomAttribute<RequiredAttribute>();
            if (required != null)
            {
                errors.Add(new FieldError
                {
                    Field = propertyName,
                    Message = GetErrorMessage("REQUIRED", $"{propertyName} is required"),
                    Code = ValidationCodes.Required
                });
            }
        }

        return new ValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors
        };
    }

    /// <summary>Get localized error message</summary>
    private string GetErrorMessage(string code, string defaultMessage)
    {
        return _errorMessages.TryGetValue(code, out var message) ? message : defaultMessage;
    }

    /// <summary>Check if string is a valid email</summary>
    private static bool IsValidEmail(string email)
    {
        try
        {
            var addr = new System.Net.Mail.MailAddress(email);
            return addr.Address == email;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>Required attribute for validation</summary>
[AttributeUsage(AttributeTargets.Property)]
internal class RequiredAttribute : Attribute
{
    public string? ErrorMessage { get; set; }
}

/// <summary>String length constraint</summary>
[AttributeUsage(AttributeTargets.Property)]
internal class StringLengthAttribute : Attribute
{
    public int MaximumLength { get; set; }
    public int MinimumLength { get; set; }

    public StringLengthAttribute(int maximumLength)
    {
        MaximumLength = maximumLength;
        MinimumLength = 0;
    }
}

/// <summary>Range constraint for numeric values</summary>
[AttributeUsage(AttributeTargets.Property)]
internal class RangeAttribute : Attribute
{
    public object Minimum { get; set; }
    public object Maximum { get; set; }

    public RangeAttribute(object minimum, object maximum)
    {
        Minimum = minimum;
        Maximum = maximum;
    }
}

/// <summary>Email address validation</summary>
[AttributeUsage(AttributeTargets.Property)]
internal class EmailAddressAttribute : Attribute
{
    public string? ErrorMessage { get; set; }
}
