using Modulus.Cli.Services;
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
                .WithExample("app", "MyApp")
                .WithExample("app", "MyApp", "--with-example")
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
            config.AddCommand<Commands.AddAnalyticsCommand>("add-analytics")
                .WithDescription("Add the analytics layer: ClickHouse sink and OpenLineage emitter wiring, settings, and a local ClickHouse and RabbitMQ compose file.")
                .WithExample("add-analytics")
                .WithExample("add-analytics", "--no-compose");
            config.AddCommand<Commands.AddMessagingCommand>("add-messaging")
                .WithDescription("Add the messaging bus to the API host: packages, bus and transport (PostgreSQL with the transactional outbox and inbox, RabbitMQ or in-memory), settings.")
                .WithExample("add-messaging")
                .WithExample("add-messaging", "--transport", "rabbitmq");
            config.AddCommand<Commands.AddWebhooksCommand>("add-webhooks")
                .WithDescription("Add outgoing webhooks: integration events delivered, signed, to tenant subscriptions (store module, host wiring, management API).")
                .WithExample("add-webhooks")
                .WithExample("add-webhooks", "--events", "catalog.product-created.v1");
            config.AddCommand<Commands.AddTenantLifecycleCommand>("add-tenant-lifecycle")
                .WithDescription("Add the tenant data lifecycle: exports of a company's data, offboarding, legal holds, retention (store module, host wiring, export endpoints). Needs multi-tenancy.")
                .WithExample("add-tenant-lifecycle");
            config.AddCommand<Commands.AddAuditStoreCommand>("add-audit-store")
                .WithDescription("Keep the business audit log and the hash-chained security audit in a database (store module, host wiring, settings).")
                .WithExample("add-audit-store");
            config.AddCommand<Commands.AddDeploymentCommand>("add-deployment")
                .WithDescription("Write the production container files: Dockerfile, docker-compose with Traefik and automatic TLS, a documented .env.example, and the token server's certificate folder.")
                .WithExample("add-deployment", "--host", "shop.example.com", "--email", "ops@example.com");
            config.AddCommand<Commands.AddAuthorizationStoreCommand>("add-authorization-store")
                .WithDescription("Move an app's authorization store to its own context with migrations (Authorization module), so production can create its schema.")
                .WithExample("add-authorization-store");
            config.AddCommand<Commands.AddOpenBaoCommand>("add-openbao")
                .WithDescription("Keep secrets in OpenBao (read-only in the API host), with --tenant-databases for per-tenant connection strings; settings and dev bootstrap.")
                .WithExample("add-openbao")
                .WithExample("add-openbao", "--auth", "approle", "--tenant-databases");
            config.AddCommand<Commands.AddRealtimeCommand>("add-realtime")
                .WithDescription("Push integration events to connected clients over SSE (and SignalR with --signalr), filtered by tenant and permission; relays /realtime through BFFs.")
                .WithExample("add-realtime")
                .WithExample("add-realtime", "--bff", "web,mobile")
                .WithExample("add-realtime", "--signalr");
            config.AddCommand<Commands.AddSearchCommand>("add-search")
                .WithDescription("Add full-text search (OpenSearch or Solr) behind ISearchIndex: package, registration and the Search settings.")
                .WithExample("add-search")
                .WithExample("add-search", "--provider", "solr");
            config.AddCommand<Commands.AddStorageCommand>("add-storage")
                .WithDescription("Move file storage to the cloud (S3, Azure Blob Storage or Google Cloud Storage): package, registration and the Storage settings.")
                .WithExample("add-storage")
                .WithExample("add-storage", "--provider", "azure");
            config.AddCommand<Commands.AddJobsCommand>("add-jobs")
                .WithDescription("Add the Quartz job scheduler behind IJobScheduler (enqueue, schedule and recurring jobs).")
                .WithExample("add-jobs");
            config.AddCommand<Commands.AddKeyVaultCommand>("add-keyvault")
                .WithDescription("Read secrets from Azure Key Vault (DefaultAzureCredential, read-only): package, registration and the Secrets settings.")
                .WithExample("add-keyvault");
            config.AddCommand<Commands.AddMediaCommand>("add-media")
                .WithDescription("Add the media library: validated uploads with tenant-scoped metadata in a store module of its own, and the /api/media endpoints.")
                .WithExample("add-media");
            config.AddCommand<Commands.AddNotificationsCommand>("add-notifications")
                .WithDescription("Add the notification inbox and channel preferences: a store module of its own and the /api/notifications endpoints.")
                .WithExample("add-notifications");
            config.AddCommand<Commands.AddLocalizationCommand>("add-localization")
                .WithDescription("Keep translations in the database (runtime-edited) instead of memory: a store module of its own.")
                .WithExample("add-localization");
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
                .WithDescription("Describe the app (hosts, modules and entities, BFFs, wired features); with --json, for tools that drive the CLI.")
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
        });

        // The update check runs alongside the command; its notice prints after the command's own output.
        var updateCheck = CliUpdateNotifier.Start(args);
        var exitCode = app.Run(args);
        CliUpdateNotifier.Finish(updateCheck, args);
        return exitCode;
    }
}

internal sealed class DefaultCommand : Command
{
    public override int Execute(CommandContext context)
    {
        AnsiConsole.Write(
            new FigletText("Modulus")
                .Color(Color.Cyan1));

        AnsiConsole.MarkupLine("[grey]APIs for .NET 10: modular monolith or microservices[/]");
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

        table.AddRow("[cyan]app[/] [grey][[<name>]][/]", "Create a new API app (modular monolith or microservices)");
        table.AddRow("[cyan]module[/] [grey]<name>[/]", "Create a standalone business module");
        table.AddRow("[cyan]add-module[/] [grey]<name>[/]", "Add a module to an existing app");
        table.AddRow("[cyan]generate-crud[/] [grey]<Entity>[/]", "Generate an entity with its CRUD handlers and endpoints");
        table.AddRow("[cyan]generate-command[/] [grey]<Name>[/]", "Generate a single command handler");
        table.AddRow("[cyan]generate-query[/] [grey]<Name>[/]", "Generate a single query handler");
        table.AddRow("[cyan]migrate add[/] [grey]<Name>[/]", "Scaffold a migration per module");
        table.AddRow("[cyan]migrate update[/]", "Apply pending migrations to each module database");
        table.AddRow("[cyan]add-bff[/] [grey]<client>[/]", "Add a web, mobile or partner backend for frontend");
        table.AddRow("[cyan]generate-bff-endpoint[/] [grey]<Name>[/]", "Add an aggregate endpoint to a BFF");
        table.AddRow("[cyan]generate-grpc[/] [grey]<Entity>[/]", "Expose an entity's CRUD over gRPC");
        table.AddRow("[cyan]generate-graphql[/] [grey]<Entity>[/]", "Expose an entity's CRUD over GraphQL");
        table.AddRow("[cyan]add-messaging[/]", "Add the messaging bus and a transport");
        table.AddRow("[cyan]add-webhooks[/]", "Deliver integration events to signed outgoing webhooks");
        table.AddRow("[cyan]add-realtime[/]", "Push integration events to clients (SSE; SignalR with --signalr)");
        table.AddRow("[cyan]add-analytics[/]", "Add the analytics layer (ClickHouse and OpenLineage)");
        table.AddRow("[cyan]add-ai[/]", "Host the AI platform's read-only connector");
        table.AddRow("[cyan]add-search[/]", "Add full-text search (OpenSearch or Solr)");
        table.AddRow("[cyan]add-storage[/]", "Move file storage to S3, Azure Blob or Google Cloud");
        table.AddRow("[cyan]add-jobs[/]", "Add the Quartz job scheduler");
        table.AddRow("[cyan]add-keyvault[/]", "Read secrets from Azure Key Vault");
        table.AddRow("[cyan]add-openbao[/]", "Read secrets from OpenBao");
        table.AddRow("[cyan]add-media[/]", "Add the media library");
        table.AddRow("[cyan]add-notifications[/]", "Add the notification inbox and channel preferences");
        table.AddRow("[cyan]add-localization[/]", "Keep translations in the database");
        table.AddRow("[cyan]add-tenant-lifecycle[/]", "Add tenant export, offboarding and retention (multi-tenant apps)");
        table.AddRow("[cyan]add-audit-store[/]", "Keep the business and security audit logs in a database");
        table.AddRow("[cyan]add-authorization-store[/]", "Give the authorization store its own migrations");
        table.AddRow("[cyan]add-deployment[/]", "Write the production container files");
        table.AddRow("[cyan]list[/]", "List this app's modules, entities and migrations");
        table.AddRow("[cyan]describe[/]", "Describe the app (--json for tools)");
        table.AddRow("[cyan]info[/]", "Overview of the host, wired features and modules");
        table.AddRow("[cyan]doctor[/]", "Check the .NET SDK, tools and app structure");
        table.AddRow("[cyan]outdated[/]", "Show outdated packages");
        table.AddRow("[cyan]update[/]", "Update packages to the latest versions");

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

