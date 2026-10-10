using System.ComponentModel;
using Modulus.Cli.Services;
using Spectre.Console.Cli;

namespace Modulus.Cli.Commands;

internal sealed class UiCreatePermissionMatrixCommand : Command<UiCreatePermissionMatrixCommand.Settings>
{
    internal sealed class Settings : ModulusSettings
    {
        [Description("Entity the matrix is for, e.g. Order (its class gives the module and the default permission)")]
        [CommandArgument(0, "<entity>")]
        public string Entity { get; init; } = "";

        [Description("Module that holds the entity")]
        [CommandOption("--module")]
        public string? Module { get; init; }

        [Description("Roles to show as rows, comma separated (default: Admin,Manager,User)")]
        [CommandOption("--roles")]
        public string? Roles { get; init; }

        [Description("Permissions to show as columns, comma separated (default: the entity's <module>:<route>:manage)")]
        [CommandOption("--permissions")]
        public string? Permissions { get; init; }

        [Description("UI engine: mvc, razor-pages or blazor (default: the one recorded in .modulus.json)")]
        [CommandOption("--engine")]
        public string? Engine { get; init; }

        [Description("App root directory (default: current directory)")]
        [CommandOption("-o|--output")]
        public string? Output { get; init; }
    }

    public override int Execute(CommandContext ctx, Settings s)
    {
        s.Apply();
        return CommandRunner.Run(() =>
        {
            var entity = CodeGen.ValidateIdentifier(s.Entity, "Entity");
            var roles = UiPermissions.ParseRoles(s.Roles);

            var start = Path.GetFullPath(s.Output ?? "./");
            var inventory = ModuleDiscovery.Inventory(start)
                ?? throw new InvalidOperationException(
                    "No .slnx found in the current directory tree. Run from inside a Modulus application, or pass --output <path>.");

            var (_, moduleName, _) = EntityMetadata.FindInApp(inventory.SolutionDir, entity, s.Module);
            var permissions = UiPermissions.ParsePermissions(
                s.Permissions, UiAccessGates.CrudPermission(moduleName, CodeGen.Pluralize(entity)));

            var route = CodeGen.ToKebabCase(entity);
            var pageName = UiPermissions.PageName(route);
            var page = new UiAuthPage("permission-matrix", pageName, $"/permissions/{route}");

            return UiPageScaffold.Run([page], "ui/PermissionsController", "PermissionsController.cs",
                s.Engine, s.Output, $"the {entity} permission matrix", folder: "Permissions", sharedController: true,
                model: new
                {
                    EntityName = entity,
                    EntityRoute = route,
                    ApiPrefix = UiPermissions.ApiPrefix,
                    Roles = roles,
                    Permissions = permissions,
                });
        });
    }
}
