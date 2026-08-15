namespace ModulusSample.Modules.VirtualFileExplorer.Domain.Events;

public sealed record FileCreatedDomainEvent(Guid EventId, Guid FileId, string FileName, string FileType, string ParentPath, DateTime CreatedAtUtc);
public sealed record FileUpdatedDomainEvent(Guid EventId, Guid FileId, string FileName, string FileType, string ParentPath, DateTime UpdatedAtUtc);
public sealed record FileDeletedDomainEvent(Guid EventId, Guid FileId, string FileName, string FileType, string ParentPath, DateTime DeletedAtUtc);
public sealed record FileMovedDomainEvent(Guid EventId, Guid FileId, string FileName, string OldPath, string NewPath, DateTime MovedAtUtc);
public sealed record FileRenamedDomainEvent(Guid EventId, Guid FileId, string OldFileName, string NewFileName, DateTime RenamedAtUtc);
public sealed record FileUploadedDomainEvent(Guid EventId, Guid FileId, string FileName, string FileType, long FileSize, string ParentPath, DateTime UploadedAtUtc);
public sealed record FileDownloadedDomainEvent(Guid EventId, Guid FileId, string FileName, string FileType, Guid DownloadedBy, DateTime DownloadedAtUtc);
public sealed record FileArchivedDomainEvent(Guid EventId, Guid FileId, string FileName, string FileType, string ParentPath, DateTime ArchivedAtUtc);
public sealed record FileRestoredDomainEvent(Guid EventId, Guid FileId, string FileName, string FileType, string ParentPath, DateTime RestoredAtUtc);
public sealed record DirectoryCreatedDomainEvent(Guid EventId, Guid DirectoryId, string DirectoryName, string ParentPath, DateTime CreatedAtUtc);
public sealed record DirectoryDeletedDomainEvent(Guid EventId, Guid DirectoryId, string DirectoryName, string ParentPath, DateTime DeletedAtUtc);
public sealed record DirectoryMovedDomainEvent(Guid EventId, Guid DirectoryId, string DirectoryName, string OldPath, string NewPath, DateTime MovedAtUtc);
public sealed record DirectoryRenamedDomainEvent(Guid EventId, Guid DirectoryId, string OldDirectoryName, string NewDirectoryName, DateTime RenamedAtUtc);
public sealed record PermissionGrantedDomainEvent(Guid EventId, Guid FileOrDirectoryId, string Name, string Permission, string GrantedTo, DateTime GrantedAtUtc);
public sealed record PermissionRevokedDomainEvent(Guid EventId, Guid FileOrDirectoryId, string Name, string Permission, string RevokedFrom, DateTime RevokedAtUtc);
public sealed record ShareCreatedDomainEvent(Guid EventId, Guid ShareId, Guid FileOrDirectoryId, string Name, string ShareType, DateTime CreatedAtUtc);
public sealed record ShareDeletedDomainEvent(Guid EventId, Guid ShareId, Guid FileOrDirectoryId, string Name, DateTime DeletedAtUtc);