using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using TradeFlow.Modules.Costing.Application.IntegrationEventHandlers;
using TradeFlow.Modules.Costing.Application.IntegrationEvents;

namespace TradeFlow.Modules.Costing.UnitTests;

[Trait("Category", "Unit")]
public sealed class CostSheetFinalizedIntegrationEventHandlerTests
{
    private readonly Mock<ILogger<CostSheetFinalizedIntegrationEventHandler>> _logger = new();
    private readonly CostSheetFinalizedIntegrationEventHandler _handler;

    public CostSheetFinalizedIntegrationEventHandlerTests()
    {
        _handler = new CostSheetFinalizedIntegrationEventHandler(_logger.Object);
    }

    [Fact]
    public async Task HandleAsync_LogsInformationAndCompletes()
    {
        var @event = new CostSheetFinalizedIntegrationEvent(
            SheetId: Guid.NewGuid(),
            TenantId: Guid.NewGuid(),
            FileId: Guid.NewGuid(),
            SheetNumber: "CS-2026-001",
            Version: 1,
            OccurredAtUtc: DateTime.UtcNow);

        Func<Task> act = async () => await _handler.HandleAsync(@event, CancellationToken.None);

        await act.Should().NotThrowAsync();
        _logger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("CS-2026-001")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_CancellationToken_DoesNotThrow()
    {
        var @event = new CostSheetFinalizedIntegrationEvent(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "CS-001", 2, DateTime.UtcNow);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = async () => await _handler.HandleAsync(@event, cts.Token);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void Event_HasCorrectType()
    {
        var @event = new CostSheetFinalizedIntegrationEvent(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "CS-001", 1, DateTime.UtcNow);

        @event.EventType.Should().Be("Costing.CostSheetFinalized.v1");
    }

    [Fact]
    public void AdjustedEvent_HasCorrectType()
    {
        var @event = new CostSheetAdjustedIntegrationEvent(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "CS-001", 2, DateTime.UtcNow);

        @event.EventType.Should().Be("Costing.CostSheetAdjusted.v1");
    }

    [Fact]
    public void Event_PropertiesAreSet()
    {
        var sheetId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var fileId = Guid.NewGuid();

        var @event = new CostSheetFinalizedIntegrationEvent(
            sheetId, tenantId, fileId,
            "CS-2026-001", 3, new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc));

        @event.SheetId.Should().Be(sheetId);
        @event.TenantId.Should().Be(tenantId);
        @event.FileId.Should().Be(fileId);
        @event.SheetNumber.Should().Be("CS-2026-001");
        @event.Version.Should().Be(3);
        @event.OccurredAtUtc.Should().Be(new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void AdjustedEvent_PropertiesAreSet()
    {
        var sheetId = Guid.NewGuid();

        var @event = new CostSheetAdjustedIntegrationEvent(
            sheetId, Guid.NewGuid(), Guid.NewGuid(),
            "CS-001", 2, DateTime.UtcNow);

        @event.SheetId.Should().Be(sheetId);
        @event.Version.Should().Be(2);
    }
}
