using Modulus.Core.Abstractions;
using Modulus.Events.Abstractions;
using ModulusSample.Modules.VirtualFileExplorer.Application.IntegrationEvents;
using ModulusSample.Modules.VirtualFileExplorer.Domain.Events;
using Microsoft.Extensions.Logging;

namespace ModulusSample.Modules.VirtualFileExplorer.Application.DomainEventHandlers;

public sealed class VirtualFolderCreatedDomainEventHandler(
    IModuleBus moduleBus,
    ILogger<VirtualFolderCreatedDomainEventHandler> logger) : IDomainEventHandler<VirtualFolderCreatedDomainEvent>
{
    public Task HandleAsync(VirtualFolderCreatedDomainEvent @event, CancellationToken ct)
    {
        logger.LogInformation(
            "Publishing VirtualFolderCreatedIntegrationEvent - FolderId: {FolderId}, Name: {Name}",
            @event.FolderId.Value,
            @event.Name);

        var integrationEvent = new VirtualFolderCreatedIntegrationEvent(
            @event.FolderId.Value,
            @event.Name,
            @event.ParentFolderId?.Value,
            @event.TenantId);

        return moduleBus.PublishAsync(integrationEvent, ct);
    }
}

public sealed class VirtualFolderDeletedDomainEventHandler(
    IModuleBus moduleBus,
    ILogger<VirtualFolderDeletedDomainEventHandler> logger) : IDomainEventHandler<VirtualFolderDeletedDomainEvent>
{
    public Task HandleAsync(VirtualFolderDeletedDomainEvent @event, CancellationToken ct)
    {
        logger.LogInformation(
            "Publishing VirtualFolderDeletedIntegrationEvent - FolderId: {FolderId}",
            @event.FolderId.Value);

        var integrationEvent = new VirtualFolderDeletedIntegrationEvent(
            @event.FolderId.Value,
            @event.Name,
            @event.TenantId);

        return moduleBus.PublishAsync(integrationEvent, ct);
    }
}

public sealed class VirtualFileUploadedDomainEventHandler(
    IModuleBus moduleBus,
    ILogger<VirtualFileUploadedDomainEventHandler> logger) : IDomainEventHandler<VirtualFileUploadedDomainEvent>
{
    public Task HandleAsync(VirtualFileUploadedDomainEvent @event, CancellationToken ct)
    {
        logger.LogInformation(
            "Publishing VirtualFileUploadedIntegrationEvent - FileId: {FileId}, Name: {Name}",
            @event.FileId.Value,
            @event.Name);

        var integrationEvent = new VirtualFileUploadedIntegrationEvent(
            @event.FileId.Value,
            @event.Name,
            @event.FolderId.Value,
            @event.SizeBytes,
            @event.TenantId);

        return moduleBus.PublishAsync(integrationEvent, ct);
    }
}

public sealed class VirtualFileDeletedDomainEventHandler(
    IModuleBus moduleBus,
    ILogger<VirtualFileDeletedDomainEventHandler> logger) : IDomainEventHandler<VirtualFileDeletedDomainEvent>
{
    public Task HandleAsync(VirtualFileDeletedDomainEvent @event, CancellationToken ct)
    {
        logger.LogInformation(
            "Publishing VirtualFileDeletedIntegrationEvent - FileId: {FileId}",
            @event.FileId.Value);

        var integrationEvent = new VirtualFileDeletedIntegrationEvent(
            @event.FileId.Value,
            @event.Name,
            @event.FolderId.Value,
            @event.TenantId);

        return moduleBus.PublishAsync(integrationEvent, ct);
    }
}