namespace ModulusSample.Modules.VirtualFileExplorer.Application.Permissions;

public static class VirtualFileExplorerPermissions
{
    public const string Module = "VirtualFileExplorer";

    public static class Files
    {
        public const string Create = $"{Module}.Files.Create";
        public const string View = $"{Module}.Files.View";
        public const string Download = $"{Module}.Files.Download";
        public const string Edit = $"{Module}.Files.Edit";
        public const string Delete = $"{Module}.Files.Delete";
        public const string Move = $"{Module}.Files.Move";
        public const string Rename = $"{Module}.Files.Rename";
        public const string Archive = $"{Module}.Files.Archive";
        public const string Restore = $"{Module}.Files.Restore";
    }

    public static class Directories
    {
        public const string Create = $"{Module}.Directories.Create";
        public const string View = $"{Module}.Directories.View";
        public const string Edit = $"{Module}.Directories.Edit";
        public const string Delete = $"{Module}.Directories.Delete";
        public const string Move = $"{Module}.Directories.Move";
        public const string Rename = $"{Module}.Directories.Rename";
    }

    public static class Permissions
    {
        public const string Grant = $"{Module}.Permissions.Grant";
        public const string Revoke = $"{Module}.Permissions.Revoke";
        public const string View = $"{Module}.Permissions.View";
    }

    public static class Shares
    {
        public const string Create = $"{Module}.Shares.Create";
        public const string View = $"{Module}.Shares.View";
        public const string Delete = $"{Module}.Shares.Delete";
    }

    public static class AllPermissions
    {
        public const string CreateFiles = Files.Create;
        public const string ViewFiles = Files.View;
        public const string DownloadFiles = Files.Download;
        public const string EditFiles = Files.Edit;
        public const string DeleteFiles = Files.Delete;
        public const string MoveFiles = Files.Move;
        public const string RenameFiles = Files.Rename;
        public const string ArchiveFiles = Files.Archive;
        public const string RestoreFiles = Files.Restore;
        public const string CreateDirectories = Directories.Create;
        public const string ViewDirectories = Directories.View;
        public const string EditDirectories = Directories.Edit;
        public const string DeleteDirectories = Directories.Delete;
        public const string MoveDirectories = Directories.Move;
        public const string RenameDirectories = Directories.Rename;
        public const string GrantPermissions = Permissions.Grant;
        public const string RevokePermissions = Permissions.Revoke;
        public const string ViewPermissions = Permissions.View;
        public const string CreateShares = Shares.Create;
        public const string ViewShares = Shares.View;
        public const string DeleteShares = Shares.Delete;

        public static readonly string[] Values = new[]
        {
            CreateFiles, ViewFiles, DownloadFiles, EditFiles, DeleteFiles, MoveFiles, RenameFiles, ArchiveFiles, RestoreFiles,
            CreateDirectories, ViewDirectories, EditDirectories, DeleteDirectories, MoveDirectories, RenameDirectories,
            GrantPermissions, RevokePermissions, ViewPermissions,
            CreateShares, ViewShares, DeleteShares
        };
    }
}