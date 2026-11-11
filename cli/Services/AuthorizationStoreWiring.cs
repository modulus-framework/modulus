using System.Text.RegularExpressions;

namespace Modulus.Cli.Services;

/// <summary>
/// The edit <c>modulus add-authorization-store</c> makes to the API host: the framework's default authorization context
/// (<c>AddEfCoreAuthorizationStores(options =&gt; options.UseX(...))</c>) becomes the application's own store
/// (<c>AddAuthorizationStore(builder.Configuration)</c>, from the Authorization module), whose migrations the application owns.
/// </summary>
internal static partial class AuthorizationStoreWiring
{
    /// <summary>The registration call a host has when it uses the framework's default context.</summary>
    internal const string DefaultRegistrationMarker = "AddEfCoreAuthorizationStores(";

    /// <summary>The call a host has once it uses the application's own store.</summary>
    internal const string OwnStoreMarker = "AddAuthorizationStore(";

    /// <summary>True when the text registers the default context (and so has not been moved yet).</summary>
    internal static bool UsesDefaultStore(string text) => text.Contains(DefaultRegistrationMarker, StringComparison.Ordinal);

    /// <summary>
    /// Replaces the default registration with the Authorization module's call and adds its <c>using</c>. Text that does not
    /// hold the generated registration line is returned unchanged; running it again changes nothing.
    /// </summary>
    internal static string ReplaceRegistration(string text, string authorizationNamespace)
    {
        var replaced = Registration().Replace(text, "builder.Services.AddAuthorizationStore(builder.Configuration);", 1);
        if (replaced == text)
            return text;

        var use = $"using {authorizationNamespace}.Infrastructure;";
        if (replaced.Contains(use, StringComparison.Ordinal))
            return replaced;

        var nl = replaced.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var firstUsing = replaced.IndexOf("using ", StringComparison.Ordinal);
        return firstUsing >= 0
            ? replaced.Insert(firstUsing, use + nl)
            : use + nl + replaced;
    }

    [GeneratedRegex(@"builder\.Services\.AddEfCoreAuthorizationStores\(options => options\.\w+\(builder\.Configuration\.GetConnectionString\(""Authorization""\) \?\? throw new InvalidOperationException\(""ConnectionStrings:Authorization is not configured\.""\)\)\);")]
    private static partial Regex Registration();
}
