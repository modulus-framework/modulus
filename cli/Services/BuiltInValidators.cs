using System.Text.RegularExpressions;

namespace Modulus.Cli.Services;

/// <summary>String validator with common string validation rules</summary>
internal class StringValidator : IValidationRule
{
    public string RuleName => "StringValidator";

    private string _fieldName = string.Empty;
    private string _value = string.Empty;
    private int _minLength = 0;
    private int _maxLength = int.MaxValue;
    private string? _pattern;
    private string _errorMessage = string.Empty;

    public StringValidator ForField(string fieldName)
    {
        _fieldName = fieldName;
        return this;
    }

    public StringValidator NotEmpty()
    {
        _errorMessage = $"{_fieldName} cannot be empty";
        return this;
    }

    public StringValidator MinLength(int length)
    {
        _minLength = length;
        return this;
    }

    public StringValidator MaxLength(int length)
    {
        _maxLength = length;
        return this;
    }

    public StringValidator Length(int min, int max)
    {
        _minLength = min;
        _maxLength = max;
        return this;
    }

    public StringValidator Matches(string pattern)
    {
        _pattern = pattern;
        return this;
    }

    public ValidationResult Validate(object? value, ValidationContext context)
    {
        if (value == null)
            return ValidationResult.Success();

        _value = value.ToString() ?? string.Empty;

        if (string.IsNullOrEmpty(_value))
        {
            return new ValidationResult
            {
                IsValid = false,
                Errors = [new FieldError
                {
                    Field = _fieldName,
                    Message = _errorMessage,
                    Code = ValidationCodes.Required
                }]
            };
        }

        if (_value.Length < _minLength)
        {
            return ValidationResult.Failed(
                new FieldError
                {
                    Field = _fieldName,
                    Message = $"{_fieldName} must be at least {_minLength} characters",
                    Code = ValidationCodes.LengthError
                });
        }

        if (_value.Length > _maxLength)
        {
            return ValidationResult.Failed(
                new FieldError
                {
                    Field = _fieldName,
                    Message = $"{_fieldName} must be at most {_maxLength} characters",
                    Code = ValidationCodes.LengthError
                });
        }

        if (_pattern != null && !Regex.IsMatch(_value, _pattern))
        {
            return ValidationResult.Failed(
                new FieldError
                {
                    Field = _fieldName,
                    Message = $"{_fieldName} has an invalid format",
                    Code = ValidationCodes.InvalidFormat
                });
        }

        return ValidationResult.Success();
    }

    public Task<ValidationResult> ValidateAsync(object? value, ValidationContext context)
    {
        return Task.FromResult(Validate(value, context));
    }
}

/// <summary>Numeric validator for integers and decimals</summary>
internal class NumericValidator : IValidationRule
{
    public string RuleName => "NumericValidator";

    private string _fieldName = string.Empty;
    private decimal? _minValue;
    private decimal? _maxValue;
    private int _decimalPlaces = -1;

    public NumericValidator ForField(string fieldName)
    {
        _fieldName = fieldName;
        return this;
    }

    public NumericValidator GreaterThan(decimal value)
    {
        _minValue = value;
        return this;
    }

    public NumericValidator LessThan(decimal value)
    {
        _maxValue = value;
        return this;
    }

    public NumericValidator Between(decimal min, decimal max)
    {
        _minValue = min;
        _maxValue = max;
        return this;
    }

    public NumericValidator DecimalPlaces(int places)
    {
        _decimalPlaces = places;
        return this;
    }

    public ValidationResult Validate(object? value, ValidationContext context)
    {
        if (value == null)
            return ValidationResult.Success();

        if (!decimal.TryParse(value.ToString(), out var decimalValue))
        {
            return ValidationResult.Failed(
                new FieldError
                {
                    Field = _fieldName,
                    Message = $"{_fieldName} must be a valid number",
                    Code = ValidationCodes.InvalidFormat
                });
        }

        if (_minValue.HasValue && decimalValue < _minValue)
        {
            return ValidationResult.Failed(
                new FieldError
                {
                    Field = _fieldName,
                    Message = $"{_fieldName} must be greater than {_minValue}",
                    Code = ValidationCodes.RangeError
                });
        }

        if (_maxValue.HasValue && decimalValue > _maxValue)
        {
            return ValidationResult.Failed(
                new FieldError
                {
                    Field = _fieldName,
                    Message = $"{_fieldName} must be less than {_maxValue}",
                    Code = ValidationCodes.RangeError
                });
        }

        if (_decimalPlaces >= 0)
        {
            var actualPlaces = BitConverter.GetBytes(decimal.GetBits(decimalValue)[3])[2];
            if (actualPlaces > _decimalPlaces)
            {
                return ValidationResult.Failed(
                    new FieldError
                    {
                        Field = _fieldName,
                        Message = $"{_fieldName} can have at most {_decimalPlaces} decimal places",
                        Code = ValidationCodes.InvalidFormat
                    });
            }
        }

        return ValidationResult.Success();
    }

    public Task<ValidationResult> ValidateAsync(object? value, ValidationContext context)
    {
        return Task.FromResult(Validate(value, context));
    }
}

/// <summary>Email validator using standard patterns</summary>
internal class EmailValidator : IValidationRule
{
    public string RuleName => "EmailValidator";

    private string _fieldName = string.Empty;
    private const string EmailPattern = @"^[^@\s]+@[^@\s]+\.[^@\s]+$";

    public EmailValidator ForField(string fieldName)
    {
        _fieldName = fieldName;
        return this;
    }

    public ValidationResult Validate(object? value, ValidationContext context)
    {
        if (value == null)
            return ValidationResult.Success();

        var email = value.ToString() ?? string.Empty;

        if (string.IsNullOrEmpty(email))
            return ValidationResult.Success();

        if (!Regex.IsMatch(email, EmailPattern, RegexOptions.IgnoreCase))
        {
            return ValidationResult.Failed(
                new FieldError
                {
                    Field = _fieldName,
                    Message = $"{_fieldName} must be a valid email address",
                    Code = ValidationCodes.InvalidFormat
                });
        }

        return ValidationResult.Success();
    }

    public Task<ValidationResult> ValidateAsync(object? value, ValidationContext context)
    {
        return Task.FromResult(Validate(value, context));
    }
}

/// <summary>URL validator</summary>
internal class UrlValidator : IValidationRule
{
    public string RuleName => "UrlValidator";

    private string _fieldName = string.Empty;

    public UrlValidator ForField(string fieldName)
    {
        _fieldName = fieldName;
        return this;
    }

    public ValidationResult Validate(object? value, ValidationContext context)
    {
        if (value == null)
            return ValidationResult.Success();

        var url = value.ToString() ?? string.Empty;

        if (string.IsNullOrEmpty(url))
            return ValidationResult.Success();

        if (!Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            return ValidationResult.Failed(
                new FieldError
                {
                    Field = _fieldName,
                    Message = $"{_fieldName} must be a valid URL",
                    Code = ValidationCodes.InvalidFormat
                });
        }

        return ValidationResult.Success();
    }

    public Task<ValidationResult> ValidateAsync(object? value, ValidationContext context)
    {
        return Task.FromResult(Validate(value, context));
    }
}
