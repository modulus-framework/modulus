namespace Modulus.Cli.Services;

/// <summary>
/// Where a generated Web project keeps its files, so every <c>modulus ui</c> command and the <c>modulus app</c> templates agree.
/// It follows the reference layout of the garments-erp Web host:
/// <list type="bullet">
/// <item><c>Services/ApiClients/&lt;Entity&gt;ApiClient.cs</c>, with the registration in <c>Services/ApiClientExtensions.cs</c></item>
/// <item><c>Components/Navigation/Menu/&lt;Entities&gt;MenuContributor.cs</c>, found by <c>Composition/MenuRegistration.cs</c></item>
/// <item>Razor Pages: <c>Pages/&lt;Module&gt;/&lt;Entities&gt;/</c>; Blazor: <c>Components/Pages/&lt;Module&gt;/&lt;Entities&gt;/</c></item>
/// <item>MVC: <c>Controllers/&lt;Module&gt;/&lt;Entities&gt;Controller.cs</c>, with the views in <c>Views/&lt;Entities&gt;/</c> (MVC finds a
/// view by its controller's name, so the views cannot move under the module)</item>
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

    /// <summary>The MVC controller file of one module's entity, e.g. <c>Controllers/Finance/AccountsController.cs</c>.</summary>
    public static string ControllerFile(string uiDir, string module, string plural) => Path.Combine(uiDir, "Controllers", module, $"{plural}Controller.cs");
}
