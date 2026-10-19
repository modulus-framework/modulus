namespace Modulus.Authorization.Fields;

using System.Collections.Concurrent;
using System.Reflection;

/// <summary>
/// The fields a command sets, inferred from the command's own parameters, so a handler does not keep a hand-written list that can
/// fall behind the command. A parameter counts as set unless it is optional (it has a default) and its value is <c>null</c>: an
/// optional <c>ExtraProperties = null</c> changes nothing, while a required nullable parameter such as <c>Guid? OrgUnitId</c> is set
/// even when it is <c>null</c>, which is how a scope field is cleared. <c>Id</c> names the record and is never a field.
/// </summary>
public static class CommandFields
{
    private static readonly ConcurrentDictionary<Type, ParameterInfo[]> Parameters = new();

    /// <summary>The names of the fields <paramref name="command"/> sets, in parameter order.</summary>
    public static string[] SetBy<TCommand>(TCommand command) where TCommand : notnull
    {
        var type = typeof(TCommand);
        var parameters = Parameters.GetOrAdd(type, static t => t.GetConstructors()
            .OrderByDescending(c => c.GetParameters().Length)
            .First()
            .GetParameters());

        List<string> fields = [];
        foreach (var parameter in parameters)
        {
            if (string.Equals(parameter.Name, "Id", StringComparison.Ordinal) || parameter.Name is null)
                continue;

            // A parameter with no readable property cannot be shown to be unset, so it counts as set (fail closed).
            var property = type.GetProperty(parameter.Name);
            if (property is not null && parameter.HasDefaultValue && property.GetValue(command) is null)
                continue;

            fields.Add(parameter.Name);
        }

        return [.. fields];
    }
}
