namespace ModulusSample.Modules.Media.Domain.Events;

public sealed record MediaUploadedDomainEvent(Guid EventId, Guid MediaId, string FileName, string MediaType, long FileSize, DateTime UploadedAtUtc);
public sealed record MediaProcessingStartedDomainEvent(Guid EventId, Guid MediaId, string FileName, DateTime ProcessingStartedAtUtc);
public sealed record MediaProcessingCompletedDomainEvent(Guid EventId, Guid MediaId, string FileName, DateTime ProcessingCompletedAtUtc);
public sealed record MediaProcessingFailedDomainEvent(Guid EventId, Guid MediaId, string FileName, string ErrorMessage, DateTime ProcessingFailedAtUtc);
public sealed record MediaReadyDomainEvent(Guid EventId, Guid MediaId, string FileName, string MediaType, long FileSize, DateTime ReadyAtUtc);
public sealed record MediaDeletedDomainEvent(Guid EventId, Guid MediaId, string FileName, DateTime DeletedAtUtc);
public sealed record MediaArchivedDomainEvent(Guid EventId, Guid MediaId, string FileName, DateTime ArchivedAtUtc);
public sealed record MediaTypeChangedDomainEvent(Guid EventId, Guid MediaId, string FileName, string OldType, string NewType, DateTime ChangedAtUtc);
public sealed record MediaFileRenamedDomainEvent(Guid EventId, Guid MediaId, string OldFileName, string NewFileName, DateTime RenamedAtUtc);
public sealed record MediaMovedDomainEvent(Guid EventId, Guid MediaId, string FileName, string OldPath, string NewPath, DateTime MovedAtUtc);
public sealed record MediaDownloadedDomainEvent(Guid EventId, Guid MediaId, string FileName, Guid DownloadedBy, DateTime DownloadedAtUtc);