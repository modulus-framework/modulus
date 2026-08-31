using System.Text.Json;

namespace TradeFlow.Modules.WorkflowEngine.Domain.Services;

/// <summary>
/// Parses workflow definition steps JSON into a structured list.
/// Supports the JSON format defined in doc 02 §7.1.
/// </summary>
public static class WorkflowStepParser
{
    public static IReadOnlyList<WorkflowStepDefinition> Parse(string stepsJson)
    {
        if (string.IsNullOrWhiteSpace(stepsJson))
            return [];

        try
        {
            using var doc = JsonDocument.Parse(stepsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return [];

            var steps = new List<WorkflowStepDefinition>();
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                var step = ParseStep(element);
                if (step is not null)
                    steps.Add(step);
            }
            return steps;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static WorkflowStepDefinition? ParseStep(JsonElement element)
    {
        if (!element.TryGetProperty("id", out var idProp))
            return null;

        string id = idProp.GetString() ?? string.Empty;
        string type = element.TryGetProperty("type", out var typeProp) ? typeProp.GetString() ?? "approval" : "approval";
        string? when = element.TryGetProperty("when", out var whenProp) ? whenProp.GetString() : null;
        string? condition = element.TryGetProperty("condition", out var condProp) ? condProp.GetString() : null;

        int? slaHours = null;
        if (element.TryGetProperty("slaHours", out var slaProp) && slaProp.ValueKind == JsonValueKind.Number)
            slaHours = slaProp.GetInt32();

        string? assigneePosition = null;
        string? assigneeScope = null;
        if (element.TryGetProperty("assignee", out var assigneeProp) && assigneeProp.ValueKind == JsonValueKind.Object)
        {
            if (assigneeProp.TryGetProperty("position", out var posProp))
                assigneePosition = posProp.GetString();
            if (assigneeProp.TryGetProperty("scope", out var scopeProp))
                assigneeScope = scopeProp.GetString();
        }

        string? onFailRoute = null;
        bool onFailRequireReason = false;
        if (element.TryGetProperty("onFail", out var onFailProp) && onFailProp.ValueKind == JsonValueKind.Object)
        {
            if (onFailProp.TryGetProperty("route", out var routeProp))
                onFailRoute = routeProp.GetString();
            if (onFailProp.TryGetProperty("requireReason", out var reasonProp) && reasonProp.ValueKind == JsonValueKind.True)
                onFailRequireReason = true;
        }

        string? escalateToPosition = null;
        if (element.TryGetProperty("escalateTo", out var escProp) && escProp.ValueKind == JsonValueKind.Object)
        {
            if (escProp.TryGetProperty("position", out var posProp))
                escalateToPosition = posProp.GetString();
        }

        return new WorkflowStepDefinition(
            id, type, when, condition, slaHours,
            assigneePosition, assigneeScope,
            onFailRoute, onFailRequireReason,
            escalateToPosition);
    }
}

/// <summary>
/// A parsed workflow step from the definition JSON.
/// </summary>
public sealed record WorkflowStepDefinition(
    string Id,
    string Type,
    string? When,
    string? Condition,
    int? SlaHours,
    string? AssigneePosition,
    string? AssigneeScope,
    string? OnFailRoute,
    bool OnFailRequireReason,
    string? EscalateToPosition);
