using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.VirtualFileExplorer.Domain.Constants;

public static class Schemas
{
    public const string VirtualFileExplorer = "virtual_file_explorer";
}

public static class FileTypes
{
    public const string File = "file";
    public const string Directory = "directory";
    public const string Link = "link";
}

public static class FileStatuses
{
    public const string Active = "active";
    public const string Archived = "archived";
    public const string Deleted = "deleted";
    public const string Uploading = "uploading";
}

public static class VirtualFileErrors
{
    public static readonly Error NotFound = Error.NotFound("VirtualFile.NotFound", "File not found");
    public static readonly Error DuplicateName = Error.Conflict("VirtualFile.DuplicateName", "A file with this name already exists in the same location");
    public static readonly Error EmptyName = Error.Validation("VirtualFile.EmptyName", "File name cannot be empty");
    public static readonly Error InvalidParent = Error.Validation("VirtualFile.InvalidParent", "Invalid parent directory");
    public static readonly Error InvalidType = Error.Validation("VirtualFile.InvalidType", "Invalid file type");
    public static readonly Error CannotDeleteRoot = Error.BusinessRule("VirtualFile.CannotDeleteRoot", "Cannot delete root directory");
    public static readonly Error CannotDeleteDirectoryWithContents = Error.BusinessRule("VirtualFile.CannotDeleteDirectoryWithContents", "Cannot delete non-empty directory");
    public static readonly Error InvalidPath = Error.Validation("VirtualFile.InvalidPath", "Invalid file path");
    public static readonly Error PathTooLong = Error.Validation("VirtualFile.PathTooLong", "Path exceeds maximum length");
    public static readonly Error InvalidFileName = Error.Validation("VirtualFile.InvalidFileName", "File name contains invalid characters");
    public static readonly Error FileSizeExceeded = Error.Validation("VirtualFile.FileSizeExceeded", "File size exceeds limit");
    public const long MaxFileSizeBytes = 1048576000; // 1GB
    public static readonly Error CircularReference = Error.BusinessRule("VirtualFile.CircularReference", "Circular reference detected");
}