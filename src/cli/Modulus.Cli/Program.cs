using Spectre.Console;
using Spectre.Console.Cli;

namespace Modulus.Cli;

public static class Program
{
    public static int Main(string[] args)
    {
        var app = new CommandApp<DefaultCommand>();
        app.Configure(config =>
        {
            config.SetApplicationName("modulus");
            config.ValidateExamples();

            config.AddCommand<Commands.NewAppCommand>("app")
                .WithDescription("Create a new Modulus application.")
                .WithExample("app", "MyCompany.MyApp")
                .WithExample("app", "MyApp", "--database", "SqlServer");

            config.AddCommand<Commands.NewModuleCommand>("module")
                .WithDescription("Create a new business module.")
                .WithExample("module", "Catalog", "--app", "MyApp.Host");

            config.AddCommand<Commands.AddModuleCommand>("add-module")
                .WithDescription("Add a business module to an existing application.")
                .WithExample("add-module", "Orders");

            config.AddCommand<Commands.GenerateCrudCommand>("generate-crud")
                .WithDescription("Generate CRUD endpoints, handlers, and entity for a domain object.")
                .WithExample("generate-crud", "Product", "--module", "Catalog");
        });

        return app.Run(args);
    }
}

internal sealed class DefaultCommand : Command
{
    public override int Execute(CommandContext context)
    {
        AnsiConsole.Write(
            new FigletText("Modulus")
                .Color(Color.Cyan1));

        AnsiConsole.MarkupLine("[grey]Modular-monolith framework for .NET 10[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[yellow]Usage:[/] modulus [grey]<command>[/] [grey][[<args>]][/]");
        AnsiConsole.WriteLine();

        var table = new Table()
            .Border(TableBorder.Minimal)
            .AddColumn("[cyan]Command[/]")
            .AddColumn("[grey]Description[/]");

        table.AddRow("app <name>", "Create a new application (modular monolith)");
        table.AddRow("module <name>", "Create a new business module project");
        table.AddRow("add-module <name>", "Add a module to an existing application");
        table.AddRow("generate-crud <E>", "Generate CRUD for a domain entity");

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]Run[/] modulus <command> --help [grey]for details.[/]");

        return 0;
    }
}
