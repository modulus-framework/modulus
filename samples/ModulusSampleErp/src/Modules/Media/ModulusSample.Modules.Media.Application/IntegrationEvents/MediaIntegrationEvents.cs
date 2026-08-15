namespace ModulusSample.Modules.Media.Application.IntegrationEvents;

public sealed record MediaUploadedIntegrationEvent(Guid MediaId, string FileName, string MediaType, long FileSize, DateTime UploadedAtUtc);
public sealed record MediaReadyIntegrationEvent(Guid MediaId, string FileName, string MediaType, long FileSize, DateTime ReadyAtUtc);
public sealed record MediaDeletedIntegrationEvent(Guid MediaId, string FileName, DateTime DeletedAtUtc);
public sealed record MediaArchivedIntegrationEvent(Guid MediaId, string FileName, DateTime ArchivedAtUtc);
public sealed record MediaFileRenamedIntegrationEvent(Guid MediaId, string OldFileName, string NewFileName, DateTime RenamedAtUtc);