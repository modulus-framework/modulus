namespace Modulus.AI.Connector.Capabilities;

using System.Text.Json.Nodes;

/// <summary>The input schemas of the generated <c>Search</c> and <c>Calculate</c> capabilities.</summary>
internal static class AiQuerySchemas
{
    /// <summary>The field of a <c>Calculate</c> record that holds the group's key.</summary>
    public const string GroupField = "group";

    /// <summary>The most sort keys one search may give.</summary>
    public const int MaxSortKeys = 3;

    /// <summary>The most values an <c>in</c> filter may list.</summary>
    public const int MaxInValues = 100;

    private static readonly string[] Operators =
        ["eq", "ne", "gt", "ge", "lt", "le", "contains", "startsWith", "in", "isNull", "isNotNull"];

    private static readonly string[] Aggregates = ["count", "sum", "average", "min", "max"];

    public static JsonNode Search(AiQueryableDescriptor queryable, ModulusAiConnectorOptions options)
        => new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = new JsonObject
            {
                ["filters"] = Filters(queryable, options),
                ["sort"] = new JsonObject
                {
                    ["type"] = "array",
                    ["maxItems"] = MaxSortKeys,
                    ["items"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["additionalProperties"] = false,
                        ["required"] = new JsonArray("field"),
                        ["properties"] = new JsonObject
                        {
                            ["field"] = FieldEnum(queryable),
                            ["descending"] = new JsonObject { ["type"] = "boolean" },
                        },
                    },
                },
                ["take"] = new JsonObject
                {
                    ["type"] = "integer",
                    ["minimum"] = 1,
                    ["maximum"] = options.MaxResults,
                },
            },
        };

    public static JsonNode Calculate(AiQueryableDescriptor queryable, ModulusAiConnectorOptions options)
        => new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray("aggregate"),
            ["properties"] = new JsonObject
            {
                ["aggregate"] = Enum(Aggregates),
                ["field"] = FieldEnum(queryable, "The field to aggregate; required except for count. Sum and average need a number."),
                ["groupBy"] = FieldEnum(queryable, $"Groups the result by this field (at most {options.MaxGroups} groups, largest first)."),
                ["filters"] = Filters(queryable, options),
            },
        };

    private static JsonObject Filters(AiQueryableDescriptor queryable, ModulusAiConnectorOptions options)
        => new()
        {
            ["type"] = "array",
            ["maxItems"] = options.MaxFilters,
            ["description"] = "Every filter must match. gt/ge/lt/le compare numbers and dates; contains/startsWith match text; "
                + $"in takes an array of at most {MaxInValues} values; isNull and isNotNull take no value.",
            ["items"] = new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["required"] = new JsonArray("field", "op"),
                ["properties"] = new JsonObject
                {
                    ["field"] = FieldEnum(queryable),
                    ["op"] = Enum(Operators),
                    ["value"] = new JsonObject { ["description"] = "The value to compare with, typed like the field." },
                },
            },
        };

    private static JsonObject FieldEnum(AiQueryableDescriptor queryable, string? description = null)
    {
        var node = Enum(queryable.Fields.Keys.Order(StringComparer.Ordinal));
        node.Insert(0, "description", description is null
            ? "One of: " + string.Join(", ", queryable.Fields.Values.Select(f => $"{f.Name} ({f.Type})"))
            : description + " One of: " + string.Join(", ", queryable.Fields.Values.Select(f => $"{f.Name} ({f.Type})")));
        return node;
    }

    private static JsonObject Enum(IEnumerable<string> values)
        => new() { ["enum"] = new JsonArray([.. values.Select(v => (JsonNode?)v)]) };
}
