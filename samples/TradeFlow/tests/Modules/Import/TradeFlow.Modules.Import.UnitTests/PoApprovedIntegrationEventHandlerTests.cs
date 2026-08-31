using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using TradeFlow.Modules.Import.Application.IntegrationEventHandlers;
using TradeFlow.Modules.Import.Application.IntegrationEvents;

namespace TradeFlow.Modules.Import.UnitTests;

[Trait("Category", "Unit")]
public sealed class PoApprovedIntegrationEventHandlerTests
{
    private readonly Mock<ILogger<PoApprovedIntegrationEventHandler>> _logger = new();
    private readonly PoApprovedIntegrationEventHandler _handler;

    public PoApprovedIntegrationEventHandlerTests()
    {
        _handler = new PoApprovedIntegrationEventHandler(_logger.Object);
    }

    [Fact]
    public async Task HandleAsync_LogsInformationAndCompletes()
    {
        var @event = new PoApprovedIntegrationEvent(
            PoId: Guid.NewGuid(),
            TenantId: Guid.NewGuid(),
            CompanyId: Guid.NewGuid(),
            PoNumber: "PO-2026-001",
            TotalAmount: 15000m,
            Currency: "USD",
            Incoterm: "FOB",
            PortOfLoading: "Chittagong",
            PortOfDischarge: "Dhaka",
            OccurredAtUtc: DateTime.UtcNow);

        Func<Task> act = async () => await _handler.HandleAsync(@event, CancellationToken.None);

        await act.Should().NotThrowAsync();
        _logger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("PO-2026-001")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_CancellationToken_DoesNotThrow()
    {
        var @event = new PoApprovedIntegrationEvent(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "PO-001", 100m, "BDT", "CIF", "Chattogram", "Dhaka", DateTime.UtcNow);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = async () => await _handler.HandleAsync(@event, cts.Token);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void Event_HasCorrectType()
    {
        var @event = new PoApprovedIntegrationEvent(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "PO-001", 100m, "BDT", "CIF", "Chattogram", "Dhaka", DateTime.UtcNow);

        @event.EventType.Should().Be("Procurement.PoApproved.v1");
    }

    [Fact]
    public void Event_PropertiesAreSet()
    {
        var poId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var companyId = Guid.NewGuid();

        var @event = new PoApprovedIntegrationEvent(
            poId, tenantId, companyId,
            "PO-001", 5000m, "USD", "FOB",
            "Chattogram", "Dhaka", new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc));

        @event.PoId.Should().Be(poId);
        @event.TenantId.Should().Be(tenantId);
        @event.CompanyId.Should().Be(companyId);
        @event.PoNumber.Should().Be("PO-001");
        @event.TotalAmount.Should().Be(5000m);
        @event.Currency.Should().Be("USD");
        @event.Incoterm.Should().Be("FOB");
        @event.PortOfLoading.Should().Be("Chattogram");
        @event.PortOfDischarge.Should().Be("Dhaka");
        @event.OccurredAtUtc.Should().Be(new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc));
    }
}
