using System.Globalization;
using System.Text.Json;

namespace TradeFlow.Modules.WorkflowEngine.Domain.Services;

/// <summary>
/// Evaluates simple conditions against workflow context variables.
/// Supports operators: >=, <=, >, <, ==, != with numeric and string comparisons.
/// No arbitrary code execution — restricted expression evaluator per doc 02 §7.2.
/// </summary>
public static class ConditionEvaluator
{
    /// <summary>
    /// Evaluates a condition string against the given context JSON.
    /// Returns true if the condition is met, false otherwise.
    /// Returns null if the condition cannot be evaluated (missing variable, parse error).
    /// </summary>
    public static bool? Evaluate(string? condition, string? contextJson)
    {
        if (string.IsNullOrWhiteSpace(condition))
            return true; // No condition = always passes

        if (string.IsNullOrWhiteSpace(contextJson))
            return null;

        try
        {
            using var contextDoc = JsonDocument.Parse(contextJson);
            var context = contextDoc.RootElement;

            // Parse condition: "variable operator value"
            // Examples: "amountBDT > 500000", "feasibilityScore >= 60", "isImport == true"
            var (variableName, op, expectedValue) = ParseCondition(condition);
            if (variableName is null)
                return null;

            if (!context.TryGetProperty(variableName, out var actualValue))
                return null;

            return CompareValues(actualValue, op, expectedValue);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static (string? Variable, string Operator, string Expected) ParseCondition(string condition)
    {
        string[] operators = [">=", "<=", "!=", "==", ">", "<"];
        foreach (string op in operators)
        {
            int idx = condition.IndexOf(op, StringComparison.Ordinal);
            if (idx >= 0)
            {
                string variable = condition[..idx].Trim();
                string expected = condition[(idx + op.Length)..].Trim();
                return (variable, op, expected);
            }
        }
        return (null, string.Empty, string.Empty);
    }

    private static bool? CompareValues(JsonElement actual, string op, string expected)
    {
        // Try numeric comparison first
        if (actual.ValueKind == JsonValueKind.Number && decimal.TryParse(expected, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal expectedNum))
        {
            decimal actualNum = actual.GetDecimal();
            return op switch
            {
                ">=" => actualNum >= expectedNum,
                "<=" => actualNum <= expectedNum,
                ">" => actualNum > expectedNum,
                "<" => actualNum < expectedNum,
                "==" => actualNum == expectedNum,
                "!=" => actualNum != expectedNum,
                _ => null
            };
        }

        // Try boolean comparison
        if (actual.ValueKind == JsonValueKind.True || actual.ValueKind == JsonValueKind.False)
        {
            bool actualBool = actual.ValueKind == JsonValueKind.True;
            if (bool.TryParse(expected, out bool expectedBool))
            {
                return op switch
                {
                    "==" => actualBool == expectedBool,
                    "!=" => actualBool != expectedBool,
                    _ => null
                };
            }
        }

        // String comparison
        if (actual.ValueKind == JsonValueKind.String)
        {
            string? actualStr = actual.GetString();
            return op switch
            {
                "==" => string.Equals(actualStr, expected, StringComparison.OrdinalIgnoreCase),
                "!=" => !string.Equals(actualStr, expected, StringComparison.OrdinalIgnoreCase),
                _ => null
            };
        }

        return null;
    }
}
