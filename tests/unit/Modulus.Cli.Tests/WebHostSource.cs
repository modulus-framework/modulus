using Modulus.Cli.Services;

namespace Modulus.Cli.Tests;

/// <summary>
/// The source of a Razor Pages or MVC web host as the generator writes it: Program.cs, which only composes, and the setup files
/// it composes (Configuration, Security, Middleware, Endpoints, Observability). Assertions about the host read this text.
/// </summary>
internal static class WebHostSource
{
    public static string Render(TemplateEngine engine, AppModel model)
    {
        var parts = new List<string>
        {
            engine.Render("app/Program.Web", model),
            engine.Render("app/WebCoreSetup.Web", model),
            engine.Render("app/UiSetup.Web", model),
            engine.Render("app/PipelineSetup.Web", model),
            engine.Render("app/WebEndpoints.Web", model),
            engine.Render("app/HealthChecksSetup.Web", model),
        };
        if (model.UseAuth)
            parts.Add(engine.Render("app/AuthenticationSetup.Web", model));
        return string.Join("\n", parts);
    }
}
