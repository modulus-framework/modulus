namespace Modulus.Cli.Services;

/// <summary>
/// Service for async validation - checking values against server-side rules
/// Supports username availability, email uniqueness, and custom async validators
/// </summary>
internal class AsyncValidationService
{
    private readonly Dictionary<string, IAsyncValidator> _validators = [];

    /// <summary>Interface for async validators</summary>
    public interface IAsyncValidator
    {
        Task<ValidationResult> ValidateAsync(string value, string fieldName);
    }

    /// <summary>Register an async validator</summary>
    public void RegisterValidator(string fieldName, IAsyncValidator validator)
    {
        _validators[fieldName] = validator;
    }

    /// <summary>Validate a field value asynchronously</summary>
    public async Task<ValidationResult> ValidateAsync(string fieldName, string value)
    {
        if (!_validators.ContainsKey(fieldName))
        {
            return ValidationResult.Success();
        }

        var validator = _validators[fieldName];
        return await validator.ValidateAsync(value, fieldName);
    }

    /// <summary>Validate multiple fields</summary>
    public async Task<ValidationResult> ValidateMultipleAsync(Dictionary<string, string> fields)
    {
        var allErrors = new List<FieldError>();

        var tasks = fields.Select(kvp => ValidateAsync(kvp.Key, kvp.Value)).ToList();
        var results = await Task.WhenAll(tasks);

        foreach (var result in results)
        {
            if (!result.IsValid)
            {
                allErrors.AddRange(result.Errors);
            }
        }

        return new ValidationResult
        {
            IsValid = allErrors.Count == 0,
            Errors = allErrors
        };
    }
}

/// <summary>
/// Async validator for checking username availability
/// </summary>
internal class UsernameAvailabilityValidator : AsyncValidationService.IAsyncValidator
{
    private readonly List<string> _reservedNames = new()
    {
        "admin", "root", "system", "test", "guest", "administrator",
        "api", "www", "mail", "ftp", "support", "help"
    };

    public Task<ValidationResult> ValidateAsync(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Task.FromResult(ValidationResult.Success());
        }

        // Check reserved names
        if (_reservedNames.Contains(value.ToLower()))
        {
            return Task.FromResult(ValidationResult.Failed(
                new FieldError
                {
                    Field = fieldName,
                    Message = $"Username '{value}' is reserved and cannot be used",
                    Code = "USERNAME_RESERVED"
                }
            ));
        }

        // Check length and format
        if (value.Length < 3)
        {
            return Task.FromResult(ValidationResult.Failed(
                new FieldError
                {
                    Field = fieldName,
                    Message = "Username must be at least 3 characters long",
                    Code = "USERNAME_TOO_SHORT"
                }
            ));
        }

        if (!System.Text.RegularExpressions.Regex.IsMatch(value, "^[a-zA-Z0-9_-]+$"))
        {
            return Task.FromResult(ValidationResult.Failed(
                new FieldError
                {
                    Field = fieldName,
                    Message = "Username can only contain letters, numbers, hyphens, and underscores",
                    Code = "USERNAME_INVALID_FORMAT"
                }
            ));
        }

        // In real scenario, would check database
        return Task.FromResult(ValidationResult.Success());
    }
}

/// <summary>
/// Async validator for checking email uniqueness
/// </summary>
internal class EmailUniquenessValidator : AsyncValidationService.IAsyncValidator
{
    private readonly HashSet<string> _existingEmails = new(StringComparer.OrdinalIgnoreCase)
    {
        "test@example.com",
        "admin@example.com",
        "user@example.com"
    };

    public Task<ValidationResult> ValidateAsync(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Task.FromResult(ValidationResult.Success());
        }

        // Basic email format check
        if (!System.Net.Mail.MailAddress.TryCreate(value, out _))
        {
            return Task.FromResult(ValidationResult.Failed(
                new FieldError
                {
                    Field = fieldName,
                    Message = "Email format is invalid",
                    Code = "EMAIL_INVALID_FORMAT"
                }
            ));
        }

        // Check if email exists (in real scenario, would query database)
        if (_existingEmails.Contains(value))
        {
            return Task.FromResult(ValidationResult.Failed(
                new FieldError
                {
                    Field = fieldName,
                    Message = "This email is already registered",
                    Code = "EMAIL_ALREADY_EXISTS"
                }
            ));
        }

        return Task.FromResult(ValidationResult.Success());
    }
}

/// <summary>
/// Custom async validator with predicate function
/// </summary>
internal class CustomAsyncValidator : AsyncValidationService.IAsyncValidator
{
    private readonly Func<string, Task<bool>> _validationFunc;
    private readonly string _errorMessage;
    private readonly string _errorCode;

    public CustomAsyncValidator(
        Func<string, Task<bool>> validationFunc,
        string errorMessage,
        string errorCode = "CUSTOM_ASYNC_ERROR")
    {
        _validationFunc = validationFunc;
        _errorMessage = errorMessage;
        _errorCode = errorCode;
    }

    public async Task<ValidationResult> ValidateAsync(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ValidationResult.Success();
        }

        var isValid = await _validationFunc(value);

        if (!isValid)
        {
            return ValidationResult.Failed(
                new FieldError
                {
                    Field = fieldName,
                    Message = _errorMessage,
                    Code = _errorCode
                }
            );
        }

        return ValidationResult.Success();
    }
}

/// <summary>
/// Async validator for URL availability (checks if URL is accessible)
/// </summary>
internal class UrlAvailabilityValidator : AsyncValidationService.IAsyncValidator
{
    public async Task<ValidationResult> ValidateAsync(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ValidationResult.Success();
        }

        // Validate URL format
        if (!Uri.TryCreate(value, UriKind.Absolute, out _))
        {
            return ValidationResult.Failed(
                new FieldError
                {
                    Field = fieldName,
                    Message = "Invalid URL format",
                    Code = "URL_INVALID_FORMAT"
                }
            );
        }

        // In real scenario, would check URL accessibility
        // For now, just validate format
        return ValidationResult.Success();
    }
}
