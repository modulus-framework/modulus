using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.VirtualFileExplorer.Domain.Constants;

public static class VirtualFileExplorerErrors
{
    public static readonly Error FolderNotFound =
        Error.NotFound("VirtualFileExplorer.FolderNotFound", "Folder not found");

    public static readonly Error FolderAlreadyExists =
        Error.Conflict("VirtualFileExplorer.FolderAlreadyExists", "A folder with this name already exists in the parent");

    public static readonly Error FolderNotEmpty =
        Error.Conflict("VirtualFileExplorer.FolderNotEmpty", "Folder is not empty; move or delete its contents first");

    public static readonly Error FileNotFound =
        Error.NotFound("VirtualFileExplorer.FileNotFound", "File not found");

    public static readonly Error FileAlreadyExists =
        Error.Conflict("VirtualFileExplorer.FileAlreadyExists", "A file with this name already exists in the folder");

    public static readonly Error FileTooLarge =
        Error.Validation("VirtualFileExplorer.FileTooLarge", "File exceeds the maximum allowed size");

    public static readonly Error EmptyFile =
        Error.Validation("VirtualFileExplorer.EmptyFile", "Cannot upload an empty file");
}