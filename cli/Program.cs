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

            // ── Scaffolding ────────────────────────────────────────────
            config.AddCommand<Commands.NewAppCommand>("app")
                .WithDescription("Create a new Modulus application with comprehensive interactive configuration.")
                .WithExample("app", "MyCompany.MyApp")
                .WithExample("app", "MyApp", "--database", "SqlServer")
                .WithExample("app", "MyApp", "--no-example")
                .WithExample("app", "MyApp", "--message-broker", "rabbitmq", "--caching", "redis")
                .WithExample("app", "MyApp", "--storage", "s3", "--enable-feature-flags")
                .WithExample("app") // interactive wizard
                ;

            config.AddCommand<Commands.NewModuleCommand>("module")
                .WithDescription("Create a new standalone business module.")
                .WithExample("module", "Catalog", "--app", "MyApp");

            config.AddCommand<Commands.AddModuleCommand>("add-module")
                .WithDescription("Add a business module to an existing application.")
                .WithExample("add-module", "Orders");

            // ── Code generation ────────────────────────────────────────
            config.AddCommand<Commands.GenerateCrudCommand>("generate-crud")
                .WithDescription("Generate CRUD endpoints, handlers, and entity for a domain object.")
                .WithExample("generate-crud", "Product", "--module", "Catalog");

            config.AddCommand<Commands.GenerateCommandCommand>("generate-command")
                .WithDescription("Generate a single command handler in a module.")
                .WithExample("generate-command", "PublishOrder", "--module", "Orders");

            config.AddCommand<Commands.GenerateQueryCommand>("generate-query")
                .WithDescription("Generate a single query handler in a module.")
                .WithExample("generate-query", "GetOrderDetails", "--module", "Orders");

            // ── EF Core migrations (per-module) ────────────────────────
            config.AddBranch("migrate", migrate =>
            {
                migrate.SetDescription("Author and apply EF Core migrations per module.");

                migrate.AddCommand<Commands.MigrateAddCommand>("add")
                    .WithDescription("Scaffold a migration in each module's Infrastructure project.")
                    .WithExample("migrate", "add", "InitialCreate")
                    .WithExample("migrate", "add", "AddOrderTotals", "--module", "Orders");

                migrate.AddCommand<Commands.MigrateUpdateCommand>("update")
                    .WithDescription("Apply pending migrations to each module's database.")
                    .WithExample("migrate", "update")
                    .WithExample("migrate", "update", "--module", "Orders");
            });

            // ── Introspection ──────────────────────────────────────────
            config.AddCommand<Commands.ListCommand>("list")
                .WithDescription("List every business module in this app (provider, entities, migrations).")
                .WithExample("list");

            config.AddCommand<Commands.InfoCommand>("info")
                .WithDescription("Show an overview of this Modulus app (host, features, modules).")
                .WithExample("info");

            config.AddCommand<Commands.DoctorCommand>("doctor")
                .WithDescription("Check the .NET SDK, dotnet-ef tool, and that this app is well-formed.")
                .WithExample("doctor");
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

        // ── Quick start panel ─────────────────────────────────────────
        var panel = new Panel(new Rows(
            new Markup("[yellow]Get started:[/]"),
            new Markup("  [grey]$[/] modulus app MyApp           [grey dim]# new app (interactive)[/]"),
            new Markup("  [grey]$[/] modulus app                  [grey dim]# full interactive wizard[/]"),
            new Markup("  [grey]$[/] modulus add-module Orders     [grey dim]# add a module[/]"),
            new Markup("  [grey]$[/] modulus generate-crud Order --module Orders"),
            new Markup("  [grey]$[/] modulus list                  [grey dim]# inspect your app[/]"),
            new Markup("  [grey]$[/] modulus doctor                [grey dim]# check your env[/]")))
            .RoundedBorder()
            .Header("[cyan]Quick start[/]");
        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();

        // ── Command reference ─────────────────────────────────────────
        var table = new Table()
            .Border(TableBorder.Minimal)
            .AddColumn("[cyan]Command[/]")
            .AddColumn("[grey]Description[/]");

        table.AddRow("[cyan]app[/] [grey][[<name>]][/]", "Create a new application (comprehensive interactive wizard)");
        table.AddRow("[cyan]module[/] [grey][[<name>]][/]", "Create a new standalone business module");
        table.AddRow("[cyan]add-module[/] [grey][[<name>]][/]", "Add a module to an existing application");
        table.AddRow("[cyan]generate-crud[/] [grey]<E>[/]", "Generate CRUD for a domain entity");
        table.AddRow("[cyan]generate-command[/] [grey]<C>[/]", "Generate a single command handler");
        table.AddRow("[cyan]generate-query[/] [grey]<Q>[/]", "Generate a single query handler");
        table.AddRow("[cyan]migrate add[/] [grey]<name>[/]", "Scaffold an EF Core migration per module");
        table.AddRow("[cyan]migrate update[/]", "Apply pending migrations to each module DB");
        table.AddRow("[cyan]list[/]", "List this app's modules + entities + migrations");
        table.AddRow("[cyan]info[/]", "Overview: host, framework features wired, modules");
        table.AddRow("[cyan]doctor[/]", "Check .NET SDK / dotnet-ef / app structure");

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

        // ── Global flags ──────────────────────────────────────────────
        AnsiConsole.MarkupLine("[yellow]Global flags[/] (apply to any command):");
        AnsiConsole.MarkupLine("  [grey]--dry-run[/]   Preview without writing files or running dotnet ef");
        AnsiConsole.MarkupLine("  [grey]--force[/]     Overwrite without prompting / skip confirmations");
        AnsiConsole.MarkupLine("  [grey]-v / --verbose[/]  Show detailed output");
        AnsiConsole.MarkupLine("  [grey]-q / --quiet[/]    Suppress everything but errors and the summary");
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine("[grey]Run[/] modulus <command> --help [grey]for details.[/]");

        return 0;
    }
}
