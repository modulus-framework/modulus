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
                .WithExample("app", "MyApp", "--kind", "api")
                .WithExample("app", "MyApp", "--kind", "web", "--ui-modules", "identity,users")
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

            config.AddCommand<Commands.AddBffCommand>("add-bff")
                .WithDescription("Add a Backend for Frontend (web, mobile or partner) to an existing application.")
                .WithExample("add-bff", "mobile");
            config.AddCommand<Commands.GenerateBffEndpointCommand>("generate-bff-endpoint")
                .WithDescription("Scaffold an aggregate endpoint in a BFF that composes several modules' data for one screen.")
                .WithExample("generate-bff-endpoint", "Dashboard", "--bff", "mobile", "--modules", "Catalog,Orders");
            config.AddCommand<Commands.GenerateGrpcCommand>("generate-grpc")
                .WithDescription("Expose an entity's CRUD commands and queries over gRPC (contract, service, host wiring, optional BFF clients).")
                .WithExample("generate-grpc", "Product", "--module", "Catalog", "--bff", "mobile");
            config.AddCommand<Commands.GenerateGraphQLCommand>("generate-graphql")
                .WithDescription("Expose an entity's CRUD commands and queries over GraphQL (graph type, module fields, host wiring, optional BFF route).")
                .WithExample("generate-graphql", "Product", "--module", "Catalog", "--bff", "mobile");
            config.AddCommand<Commands.AddWebhooksCommand>("add-webhooks")
                .WithDescription("Add outgoing webhooks: integration events delivered, signed, to tenant subscriptions (store module, host wiring, management API).")
                .WithExample("add-webhooks")
                .WithExample("add-webhooks", "--events", "catalog.product-created.v1");
            config.AddCommand<Commands.AddAuditStoreCommand>("add-audit-store")
                .WithDescription("Keep the business audit log and the hash-chained security audit in a database (store module, host wiring, settings).")
                .WithExample("add-audit-store");
            config.AddCommand<Commands.AddRealtimeCommand>("add-realtime")
                .WithDescription("Push integration events to connected clients over SSE (and SignalR with --signalr), filtered by tenant and permission; relays /realtime through BFFs.")
                .WithExample("add-realtime")
                .WithExample("add-realtime", "--bff", "web,mobile")
                .WithExample("add-realtime", "--signalr");
            config.AddCommand<Commands.AddAiCommand>("add-ai")
                .WithDescription("Host the AI platform's connector (wire contract v1) in the API host: read-only capabilities, record lookups, extraction and the change feed, behind the platform's API key and signed user envelope.")
                .WithExample("add-ai");

            // ── Code generation ────────────────────────────────────────
            config.AddCommand<Commands.GenerateCrudCommand>("generate-crud")
                .WithDescription("Generate CRUD endpoints, handlers, and entity for a domain object.")
                .WithExample("generate-crud", "Product", "--module", "Catalog")
                .WithExample("generate-crud", "Product", "--module", "Catalog", "--no-ui");

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
            config.AddCommand<Commands.DescribeCommand>("describe")
                .WithDescription("Describe the app (kind, hosts, modules and entities, BFFs, wired features); with --json, for tools that drive the CLI.")
                .WithExample("describe")
                .WithExample("describe", "--json");

            config.AddCommand<Commands.InfoCommand>("info")
                .WithDescription("Show an overview of this Modulus app (host, features, modules).")
                .WithExample("info");

            config.AddCommand<Commands.DoctorCommand>("doctor")
                .WithDescription("Check the .NET SDK, dotnet-ef tool, and that this app is well-formed.")
                .WithExample("doctor");

            // ── Version management ─────────────────────────────────────
            config.AddCommand<Commands.OutdatedCommand>("outdated")
                .WithDescription("Show outdated packages in the current application.")
                .WithExample("outdated")
                .WithExample("outdated", "--framework-only");

            config.AddCommand<Commands.UpdateCommand>("update")
                .WithDescription("Update packages to latest versions.")
                .WithExample("update")
                .WithExample("update", "--dry-run")
                .WithExample("update", "--framework-only")
                .WithExample("update", "--force");

            // ── UI Modules ──────────────────────────────────────────────
            config.AddBranch("ui", ui =>
            {
                ui.SetDescription("Manage Modulus UI modules (Razor RCLs).");

                ui.AddCommand<Commands.UiListCommand>("list")
                    .WithDescription("List all available UI modules.")
                    .WithExample("ui", "list")
                    .WithExample("ui", "list", "--installed");

                ui.AddCommand<Commands.UiSearchCommand>("search")
                    .WithDescription("Search UI modules by name, feature, or package.")
                    .WithExample("ui", "search", "identity")
                    .WithExample("ui", "search", "notifications");

                ui.AddCommand<Commands.UiInfoCommand>("info")
                    .WithDescription("Show details for a specific UI module.")
                    .WithExample("ui", "info", "Identity");

                ui.AddCommand<Commands.UiAddCommand>("add")
                    .WithDescription("Add a UI module to the current application.")
                    .WithExample("ui", "add", "Identity")
                    .WithExample("ui", "add", "Permissions", "--dry-run");

                ui.AddCommand<Commands.UiRemoveCommand>("remove")
                    .WithDescription("Remove a UI module from the current application.")
                    .WithExample("ui", "remove", "Notifications")
                    .WithExample("ui", "remove", "Files", "--force");

                ui.AddCommand<Commands.UiUpdateCommand>("update")
                    .WithDescription("Update installed UI modules to the latest version.")
                    .WithExample("ui", "update")
                    .WithExample("ui", "update", "Identity")
                    .WithExample("ui", "update", "--dry-run");

                ui.AddCommand<Commands.UiEjectCommand>("eject")
                    .WithDescription("Copy framework views (components, feature UI pages, theme layouts) into the app so they can be customized.")
                    .WithExample("ui", "eject", "--list")
                    .WithExample("ui", "eject", "Card")
                    .WithExample("ui", "eject", "Users")
                    .WithExample("ui", "eject", "Users/Details", "Tabler/Layouts/Application")
                    .WithExample("ui", "eject", "DataTable", "Input", "--force");

                ui.AddCommand<Commands.UiDiffCommand>("diff")
                    .WithDescription("Show how the app's overrides of framework views differ from the framework's current views.")
                    .WithExample("ui", "diff")
                    .WithExample("ui", "diff", "Card")
                    .WithExample("ui", "diff", "Users")
                    .WithExample("ui", "diff", "--check", "--summary");
            });
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
            new Markup("  [grey]$[/] modulus doctor                [grey dim]# check your env[/]"),
            new Markup("  [grey]$[/] modulus outdated              [grey dim]# check for updates[/]"),
            new Markup("  [grey]$[/] modulus update                [grey dim]# update packages[/]")))
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
        table.AddRow("[cyan]outdated[/]", "Show outdated packages in the current app");
        table.AddRow("[cyan]update[/]", "Update packages to latest versions");
        table.AddRow("[cyan]ui list[/]", "List all available UI modules");
        table.AddRow("[cyan]ui search[/] [grey]<term>[/]", "Search UI modules by name/feature/package");
        table.AddRow("[cyan]ui info[/] [grey]<module>[/]", "Show details for a UI module");
        table.AddRow("[cyan]ui add[/] [grey]<module>[/]", "Add a UI module to the current app");
        table.AddRow("[cyan]ui remove[/] [grey]<module>[/]", "Remove a UI module from the current app");
        table.AddRow("[cyan]ui update[/] [grey][[module]][/]", "Update installed UI modules");

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
