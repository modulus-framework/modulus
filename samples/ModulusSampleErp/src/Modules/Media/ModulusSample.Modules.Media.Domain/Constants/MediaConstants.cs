using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Media.Domain.Constants;

public static class Schemas
{
    public const string Media = "media";
}

public static class MediaTypes
{
    public const string Image = "image";
    public const string Video = "video";
    public const string Audio = "audio";
    public const string Document = "document";
    public const string Archive = "archive";
    public const string Other = "other";
}

public static class MediaStatuses
{
    public const string Uploading = "uploading";
    public const string Processing = "processing";
    public const string Ready = "ready";
    public const string Failed = "failed";
    public const string Deleted = "deleted";
}

public static class MediaErrors
{
    public static readonly Error NotFound = Error.NotFound("Media.NotFound", "Media not found");
    public static readonly Error InvalidFile = Error.Validation("Media.InvalidFile", "Invalid file");
    public static readonly Error FileTooLarge = Error.Validation("Media.FileTooLarge", "File size exceeds maximum limit");
    public const long MaxFileSizeBytes = 104857600; // 100MB
    public static readonly Error UnsupportedType = Error.Validation("Media.UnsupportedType", "Unsupported file type");
    public static readonly Error EmptyFileName = Error.Validation("Media.EmptyFileName", "File name cannot be empty");
    public static readonly Error InvalidStatus = Error.Validation("Media.InvalidStatus", "Invalid media status");
    public static readonly Error CannotDeleteSystemMedia = Error.BusinessRule("Media.CannotDeleteSystemMedia", "Cannot delete system media");
    public static readonly Error DuplicateFileName = Error.Conflict("Media.DuplicateFileName", "A media with this file name already exists");
}