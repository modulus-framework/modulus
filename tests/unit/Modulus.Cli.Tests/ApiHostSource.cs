using Modulus.Cli.Services;

namespace Modulus.Cli.Tests;

/// <summary>
/// The source of an API host as the generator writes it: Program.cs, which only composes, and the setup files it composes
/// (Composition, Configuration, Security, Hosting, Middleware, Endpoints, OpenApi). Assertions about the host read this text,
/// the same gates as <c>NewAppCommand.RenderApiSetup</c>.
/// </summary>
internal static class ApiHostSource
{
    public static string Render(TemplateEngine engine, AppModel model)
    {
        var parts = new List<string> { engine.Render("app/Program", model) };
        parts.Add(engine.Render("app/api/ModuleRegistration", model));
        parts.Add(engine.Render("app/api/CoreServicesSetup", model));
        parts.Add(engine.Render("app/api/FeatureServicesSetup", model));
        if (model.UseOpenIddict)
            parts.Add(engine.Render("app/api/TokenServerSetup", model));
        parts.Add(engine.Render("app/api/AuthenticationSetup", model));
        if (model.ExposeApi)
            parts.Add(engine.Render("app/api/OpenApiSetup", model));
        parts.Add(engine.Render("app/api/DatabaseStartup", model));
        parts.Add(engine.Render("app/api/PipelineSetup", model));
        parts.Add(engine.Render("app/api/ApiEndpoints", model));
        parts.Add(engine.Render("app/api/FeatureEndpoints", model));
        return string.Join("\n", parts);
    }
}
