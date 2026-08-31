using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using TradeFlow.Modules.Vendors.Application.IntegrationEventHandlers;
using TradeFlow.Modules.Vendors.Application.IntegrationEvents;

namespace TradeFlow.Modules.Vendors.UnitTests;

[Trait("Category", "Unit")]
public sealed class GrnPostedIntegrationEventHandlerTests
{
    private readonly Mock<ILogger<GrnPostedIntegrationEventHandler>> _logger = new();
    private readonly GrnPostedIntegrationEventHandler _handler;

    public GrnPostedIntegrationEventHandlerTests()
    {
        _handler = new GrnPostedIntegrationEventHandler(_logger.Object);
    }

    [Fact]
    public async Task HandleAsync_LogsInformationAndCompletes()
    {
        var @event = new GrnPostedIntegrationEvent(
            GrnId: Guid.NewGuid(),
            TenantId: Guid.NewGuid(),
            PoId: Guid.NewGuid(),
            VendorId: Guid.NewGuid(),
            TotalLines: 5,
            AcceptedLines: 4,
            RejectedLines: 1,
            IsOnTime: true,
            OccurredAtUtc: DateTime.UtcNow);

        Func<Task> act = async () => await _handler.HandleAsync(@event, CancellationToken.None);

        await act.Should().NotThrowAsync();
        _logger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Scorecard metrics")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_CancellationToken_DoesNotThrow()
    {
        var @event = new GrnPostedIntegrationEvent(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            3, 3, 0, true, DateTime.UtcNow);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = async () => await _handler.HandleAsync(@event, cts.Token);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void Event_HasCorrectType()
    {
        var @event = new GrnPostedIntegrationEvent(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            1, 1, 0, true, DateTime.UtcNow);

        @event.EventType.Should().Be("Inventory.GrnPosted.v1");
    }

    [Fact]
    public void Event_PropertiesAreSet()
    {
        var grnId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var poId = Guid.NewGuid();
        var vendorId = Guid.NewGuid();

        var @event = new GrnPostedIntegrationEvent(
            grnId, tenantId, poId, vendorId,
            TotalLines: 10,
            AcceptedLines: 8,
            RejectedLines: 2,
            IsOnTime: false,
            OccurredAtUtc: new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc));

        @event.GrnId.Should().Be(grnId);
        @event.TenantId.Should().Be(tenantId);
        @event.PoId.Should().Be(poId);
        @event.VendorId.Should().Be(vendorId);
        @event.TotalLines.Should().Be(10);
        @event.AcceptedLines.Should().Be(8);
        @event.RejectedLines.Should().Be(2);
        @event.IsOnTime.Should().BeFalse();
        @event.OccurredAtUtc.Should().Be(new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Event_AllAccepted_HasNoRejections()
    {
        var @event = new GrnPostedIntegrationEvent(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            TotalLines: 5, AcceptedLines: 5, RejectedLines: 0,
            IsOnTime: true, OccurredAtUtc: DateTime.UtcNow);

        @event.RejectedLines.Should().Be(0);
        @event.AcceptedLines.Should().Be(@event.TotalLines);
    }

    [Fact]
    public void Event_AllRejected_AcceptedZero()
    {
        var @event = new GrnPostedIntegrationEvent(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            TotalLines: 3, AcceptedLines: 0, RejectedLines: 3,
            IsOnTime: false, OccurredAtUtc: DateTime.UtcNow);

        @event.AcceptedLines.Should().Be(0);
        @event.RejectedLines.Should().Be(@event.TotalLines);
    }
}
