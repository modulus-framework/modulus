namespace ModulusSample.Modules.Media.Application.Permissions;

public static class MediaPermissions
{
    public const string Module = "Media";

    public static class Media
    {
        public const string Upload = $"{Module}.Media.Upload";
        public const string View = $"{Module}.Media.View";
        public const string Download = $"{Module}.Media.Download";
        public const string Edit = $"{Module}.Media.Edit";
        public const string Delete = $"{Module}.Media.Delete";
        public const string Archive = $"{Module}.Media.Archive";
        public const string Restore = $"{Module}.Media.Restore";
    }

    public static class Folders
    {
        public const string Create = $"{Module}.Folders.Create";
        public const string View = $"{Module}.Folders.View";
        public const string Edit = $"{Module}.Folders.Edit";
        public const string Delete = $"{Module}.Folders.Delete";
    }

    public static class AllPermissions
    {
        public const string UploadMedia = Media.Upload;
        public const string ViewMedia = Media.View;
        public const string DownloadMedia = Media.Download;
        public const string EditMedia = Media.Edit;
        public const string DeleteMedia = Media.Delete;
        public const string ArchiveMedia = Media.Archive;
        public const string RestoreMedia = Media.Restore;
        public const string CreateFolders = Folders.Create;
        public const string ViewFolders = Folders.View;
        public const string EditFolders = Folders.Edit;
        public const string DeleteFolders = Folders.Delete;

        public static readonly string[] Values = new[]
        {
            UploadMedia, ViewMedia, DownloadMedia, EditMedia, DeleteMedia, ArchiveMedia, RestoreMedia,
            CreateFolders, ViewFolders, EditFolders, DeleteFolders
        };
    }
}