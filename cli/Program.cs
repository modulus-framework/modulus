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
                .WithExample("app", "MyApp", "--kind", "web")
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
                ui.SetDescription("Scaffold pages and components on the Modulus UI framework.");

                ui.AddBranch("theme", theme =>
                {
                    theme.SetDescription("Create, apply, list and export app themes (Modulus:Theme values).");

                    theme.AddCommand<Commands.UiThemeCreateCommand>("create")
                        .WithDescription("Create a theme in the app's Themes folder from a base and a brand colour.")
                        .WithExample("ui", "theme", "create", "corporate", "--colors", "blue")
                        .WithExample("ui", "theme", "create", "mono", "--base", "minimal", "--colors", "#0b7285");
                    theme.AddCommand<Commands.UiThemeSetCommand>("set")
                        .WithDescription("Make a theme the app's active theme (writes Modulus:Theme in appsettings.json).")
                        .WithExample("ui", "theme", "set", "corporate");
                    theme.AddCommand<Commands.UiThemeListCommand>("list")
                        .WithDescription("List the app's themes and which one is active.")
                        .WithExample("ui", "theme", "list");
                    theme.AddCommand<Commands.UiThemeExportCommand>("export")
                        .WithDescription("Export a theme as CSS variables, SCSS variables, a Tailwind config or JSON.")
                        .WithExample("ui", "theme", "export", "corporate", "--format", "tailwind");
                });

                ui.AddCommand<Commands.UiCreateDashboardCommand>("create-dashboard")
                    .WithDescription("Scaffold a dashboard page (overview, analytics, reports, audit) from the Modulus.Ui.Templates package for the app's UI engine.")
                    .WithExample("ui", "create-dashboard", "analytics")
                    .WithExample("ui", "create-dashboard", "reports", "--module", "Admin", "--engine", "blazor");

                ui.AddCommand<Commands.UiCreateFormFromEntityCommand>("create-form-from-entity")
                    .WithDescription("Scaffold a create/edit form whose fields come from an entity's properties.")
                    .WithExample("ui", "create-form-from-entity", "Order")
                    .WithExample("ui", "create-form-from-entity", "Order", "--module", "Orders", "--engine", "blazor");

                ui.AddCommand<Commands.UiAddComponentCommand>("add-component")
                    .WithDescription("Add reusable components (alert, card, data-table, ...) to the app, for its UI engine.")
                    .WithExample("ui", "add-component", "--list")
                    .WithExample("ui", "add-component", "alert", "card", "data-table")
                    .WithExample("ui", "add-component", "--all");

                ui.AddCommand<Commands.UiAddAuthCommand>("add-auth")
                    .WithDescription("Add the forgot-password and reset-password pages (they call Modulus.Identity's /account endpoints).")
                    .WithExample("ui", "add-auth")
                    .WithExample("ui", "add-auth", "--engine", "blazor");
                ui.AddCommand<Commands.UiAdd2FaCommand>("add-2fa")
                    .WithDescription("Add the two-factor page: set up an authenticator app, recovery codes, turn off.")
                    .WithExample("ui", "add-2fa");

                ui.AddCommand<Commands.UiAddSessionManagerCommand>("add-session-manager")
                    .WithDescription("Add a page listing the signed-in user's sessions (revoke one or all) and recent sign-ins.")
                    .WithExample("ui", "add-session-manager");

                ui.AddCommand<Commands.UiCreateCrudCommand>("create-crud")
                    .WithDescription("Create list, create/edit and delete pages for an entity, over its API (generate-crud) through a typed client. Razor Pages and Blazor.")
                    .WithExample("ui", "create-crud", "Product")
                    .WithExample("ui", "create-crud", "Product", "--module", "Catalog", "--engine", "blazor");

                ui.AddCommand<Commands.UiAddAssistantCommand>("add-assistant")
                    .WithDescription("Host the AI platform's embedded assistant: /ai/session endpoint, ai:use permission, Ai:Host settings and a layout partial. Razor Pages and MVC.")
                    .WithExample("ui", "add-assistant");

                ui.AddCommand<Commands.UiAddChartCommand>("add-chart")
                    .WithDescription("Add a chart page (line, column, donut, heatmap) built on the framework's chart components, with sample data.")
                    .WithExample("ui", "add-chart", "--type", "donut")
                    .WithExample("ui", "add-chart", "--type", "line", "--engine", "blazor");

                ui.AddCommand<Commands.UiAddI18nCommand>("add-i18n")
                    .WithDescription("Set the app's languages (Modulus:Theme:Cultures) and wire request localization; shows a language picker with two or more.")
                    .WithExample("ui", "add-i18n", "--languages", "en,es,fr");

                ui.AddCommand<Commands.UiAuditCommand>("audit")
                    .WithDescription("Scan the app's Razor/HTML pages for accessibility problems (missing alt text, labels, names, lang ...).")
                    .WithExample("ui", "audit")
                    .WithExample("ui", "audit", "--format", "markdown", "--fail-on-issues");

                ui.AddCommand<Commands.UiCreatePermissionMatrixCommand>("create-permission-matrix")
                    .WithDescription("Scaffold a role/permission matrix for an entity: tick a box to grant, clear it to revoke (uses /authorization/grants).")
                    .WithExample("ui", "create-permission-matrix", "Order")
                    .WithExample("ui", "create-permission-matrix", "Order", "--roles", "Admin,Sales", "--permissions", "orders:order:manage,orders:order:export");
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
        table.AddRow("[cyan]ui theme create[/] [grey]<name>[/]", "Create an app theme from a base and a brand colour");
        table.AddRow("[cyan]ui theme set[/] [grey]<name>[/]", "Make a theme the active one (appsettings.json)");
        table.AddRow("[cyan]ui theme list[/]", "List the app's themes");
        table.AddRow("[cyan]ui theme export[/] [grey]<name>[/]", "Export a theme as css, scss, tailwind or json")
;
        table.AddRow("[cyan]ui create-dashboard[/] [grey]<template>[/]", "Scaffold an overview, analytics, reports or audit dashboard");
        table.AddRow("[cyan]ui create-form-from-entity[/] [grey]<E>[/]", "Scaffold a form from an entity's properties");
        table.AddRow("[cyan]ui add-component[/] [grey][[names]][/]", "Add UI components (alert, card, data-table, ...) to the app");
        table.AddRow("[cyan]ui add-auth[/]", "Add forgot-password and reset-password pages");
        table.AddRow("[cyan]ui add-2fa[/]", "Add the two-factor authentication page");
        table.AddRow("[cyan]ui add-session-manager[/]", "Add the sessions and sign-in history page");
        table.AddRow("[cyan]ui create-crud[/] [grey]<Entity>[/]", "Create list, edit and delete pages for an entity over its API");
        table.AddRow("[cyan]ui add-assistant[/]", "Host the AI platform's embedded assistant (session endpoint, ai:use, partial)");
        table.AddRow("[cyan]ui add-chart[/] [grey]--type T[/]", "Add a line, column, donut or heatmap chart page");
        table.AddRow("[cyan]ui add-i18n[/] [grey]--languages L[/]", "Set the app's languages and wire request localization");
        table.AddRow("[cyan]ui audit[/]", "Scan pages for accessibility problems (report as json, markdown or html)");
        table.AddRow("[cyan]ui create-permission-matrix[/] [grey]<E>[/]", "Scaffold a role/permission matrix for an entity");
        table.AddRow("[cyan]describe[/]", "Show detailed info about the current app structure");
        table.AddRow("[cyan]add-bff[/] [grey]<entity>[/]", "Expose entity CRUD over BFF (Backend for Frontend)");
        table.AddRow("[cyan]generate-bff-endpoint[/] [grey]<entity>[/]", "Generate a BFF endpoint for an entity");
        table.AddRow("[cyan]generate-grpc[/] [grey]<service>[/]", "Generate gRPC service from domain models");
        table.AddRow("[cyan]generate-graphql[/] [grey]<type>[/]", "Generate GraphQL types and resolvers");
        table.AddRow("[cyan]add-webhooks[/]", "Add webhook infrastructure (outbox, dispatcher)");
        table.AddRow("[cyan]add-audit-store[/]", "Add change auditing to entities");
        table.AddRow("[cyan]add-realtime[/]", "Add SignalR realtime messaging");
        table.AddRow("[cyan]add-ai[/]", "Add AI features (embedding, semantic search)");

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

