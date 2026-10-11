using System.Text.Json;

namespace Modulus.Cli.Services;

/// <summary>
/// Service for serializing validation results to JSON for client consumption
/// </summary>
internal class ValidationResultService
{
    /// <summary>Response format for validation results</summary>
    public class ValidationResponse
    {
        public bool IsValid { get; set; }
        public object? Data { get; set; }
        public List<ErrorDetail> Errors { get; set; } = [];
        public List<WarningDetail> Warnings { get; set; } = [];
        public string? Message { get; set; }
    }

    /// <summary>Individual error detail</summary>
    public class ErrorDetail
    {
        public string Field { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string? Code { get; set; }
        public string Severity { get; set; } = "error";
    }

    /// <summary>Individual warning detail</summary>
    public class WarningDetail
    {
        public string Field { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string? Code { get; set; }
    }

    /// <summary>Convert ValidationResult to JSON response</summary>
    public ValidationResponse CreateResponse(ValidationResult validationResult, object? data = null)
    {
        var response = new ValidationResponse
        {
            IsValid = validationResult.IsValid,
            Data = data,
            Message = validationResult.IsValid ? "Validation passed" : "Validation failed"
        };

        // Convert errors
        foreach (var error in validationResult.Errors)
        {
            response.Errors.Add(new ErrorDetail
            {
                Field = error.Field,
                Message = error.Message,
                Code = error.Code,
                Severity = error.Severity
            });
        }

        // Convert warnings
        foreach (var warning in validationResult.Warnings)
        {
            response.Warnings.Add(new WarningDetail
            {
                Field = warning.Field,
                Message = warning.Message,
                Code = warning.Code
            });
        }

        return response;
    }

    /// <summary>Serialize response to JSON</summary>
    public string SerializeResponse(ValidationResponse response)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        return JsonSerializer.Serialize(response, options);
    }

    /// <summary>Create error response</summary>
    public ValidationResponse CreateErrorResponse(string message, string errorCode = "GENERAL_ERROR")
    {
        return new ValidationResponse
        {
            IsValid = false,
            Message = message,
            Errors = new List<ErrorDetail>
            {
                new()
                {
                    Field = "general",
                    Message = message,
                    Code = errorCode,
                    Severity = "error"
                }
            }
        };
    }

    /// <summary>Create success response</summary>
    public ValidationResponse CreateSuccessResponse(object? data = null, string message = "Success")
    {
        return new ValidationResponse
        {
            IsValid = true,
            Data = data,
            Message = message
        };
    }

    /// <summary>Parse validation response from JSON</summary>
    public ValidationResponse? ParseResponse(string json)
    {
        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            return JsonSerializer.Deserialize<ValidationResponse>(json, options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Check if response indicates validation failure</summary>
    public bool HasErrors(ValidationResponse response)
    {
        return !response.IsValid || response.Errors.Count > 0;
    }

    /// <summary>Get errors for specific field</summary>
    public List<ErrorDetail> GetFieldErrors(ValidationResponse response, string fieldName)
    {
        return response.Errors
            .Where(e => e.Field.Equals(fieldName, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>Get all error messages</summary>
    public List<string> GetErrorMessages(ValidationResponse response)
    {
        return response.Errors.Select(e => e.Message).ToList();
    }

    /// <summary>Get first error message</summary>
    public string? GetFirstErrorMessage(ValidationResponse response)
    {
        return response.Errors.FirstOrDefault()?.Message;
    }
}
