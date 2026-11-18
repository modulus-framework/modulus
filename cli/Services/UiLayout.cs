namespace Modulus.Cli.Services;

/// <summary>
/// Where a generated Web project keeps its files, so every <c>modulus ui</c> command and the <c>modulus app</c> templates agree.
/// It follows the reference layout of a generated Web host:
/// <list type="bullet">
/// <item><c>Services/ApiClients/&lt;Entity&gt;ApiClient.cs</c>, with the registration in <c>Services/ApiClientExtensions.cs</c></item>
/// <item><c>Components/Navigation/Menu/&lt;Entities&gt;MenuContributor.cs</c>, found by <c>Composition/MenuRegistration.cs</c></item>
/// <item>Razor Pages: <c>Pages/&lt;Module&gt;/&lt;Entities&gt;/</c>; Blazor: <c>Components/Pages/&lt;Module&gt;/&lt;Entities&gt;/</c></item>
/// <item>MVC: one area per business module, <c>Areas/&lt;Module&gt;/{Controllers,Models,Views/&lt;Entities&gt;}</c></item>
/// </list>
/// </summary>
internal static class UiLayout
{
    /// <summary>The Web project's client registration file, relative to the Web project (for messages).</summary>
    public const string ApiClientExtensionsFile = "Services/ApiClientExtensions.cs";

    public static string ApiClientsDir(string uiDir) => Path.Combine(uiDir, "Services", "ApiClients");

    public static string ApiClientFile(string uiDir, string entity) => Path.Combine(ApiClientsDir(uiDir), $"{entity}ApiClient.cs");

    public static string ApiClientExtensions(string uiDir) => Path.Combine(uiDir, "Services", "ApiClientExtensions.cs");

    public static string MenuFile(string uiDir, string plural) => Path.Combine(uiDir, "Components", "Navigation", "Menu", $"{plural}MenuContributor.cs");

    public static string MenuNamespace(string uiNamespace) => $"{uiNamespace}.Components.Navigation.Menu";

    /// <summary>The Razor Pages folder of one module's entity, e.g. <c>Pages/Finance/Accounts</c>.</summary>
    public static string PagesFolder(string uiDir, string module, string plural) => Path.Combine(uiDir, "Pages", module, plural);

    /// <summary>The Blazor folder of one module's entity, e.g. <c>Components/Pages/Finance/Accounts</c>.</summary>
    public static string BlazorFolder(string uiDir, string module, string plural) => Path.Combine(uiDir, "Components", "Pages", module, plural);

    /// <summary>The MVC area of one business module, e.g. <c>Areas/Finance</c>.</summary>
    public static string MvcArea(string uiDir, string module) => Path.Combine(uiDir, "Areas", module);

    /// <summary>The MVC controller file of one module's entity, e.g. <c>Areas/Finance/Controllers/AccountsController.cs</c>.</summary>
    public static string ControllerFile(string uiDir, string module, string plural) => Path.Combine(MvcArea(uiDir, module), "Controllers", $"{plural}Controller.cs");

    /// <summary>The view folder of one module's entity, e.g. <c>Areas/Finance/Views/Accounts</c>.</summary>
    public static string MvcViewsFolder(string uiDir, string module, string plural) => Path.Combine(MvcArea(uiDir, module), "Views", plural);

    /// <summary>The view models file of one module's entity.</summary>
    public static string MvcViewModelsFile(string uiDir, string module, string entity) => Path.Combine(MvcArea(uiDir, module), "Models", $"{entity}ViewModels.cs");
}
