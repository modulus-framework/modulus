using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Customs.Domain.Entities;

/// <summary>
/// Port-stage demurrage accrual auto-computed daily after free time (BR-CUS-04).
/// </summary>
public sealed class DemurrageAccrual : AggregateRoot
{
    private DemurrageAccrual() { }

    private DemurrageAccrual(Guid id, Guid tenantId, Guid? fileId, string containerRef, string portCode,
        DateOnly landingDate, int freeDays, decimal dailyRateBdt)
    {
        Id = id;
        TenantId = tenantId;
        FileId = fileId;
        ContainerRef = containerRef;
        PortCode = portCode;
        LandingDate = landingDate;
        FreeDays = freeDays;
        DailyRateBdt = dailyRateBdt;
    }

    public Guid TenantId { get; private set; }
    public Guid? FileId { get; private set; }
    public string ContainerRef { get; private set; } = null!;
    public string PortCode { get; private set; } = null!;
    public DateOnly LandingDate { get; private set; }
    public int FreeDays { get; private set; }
    public decimal DailyRateBdt { get; private set; }
    public int AccruedDays { get; private set; }
    public decimal AccruedAmountBdt { get; private set; }

    public static DemurrageAccrual Create(Guid tenantId, Guid? fileId, string containerRef, string portCode,
        DateOnly landingDate, int freeDays, decimal dailyRateBdt)
    {
        if (string.IsNullOrWhiteSpace(containerRef))
            throw new ArgumentException("Container reference is required", nameof(containerRef));
        if (freeDays < 0)
            throw new ArgumentOutOfRangeException(nameof(freeDays));
        if (dailyRateBdt < 0m)
            throw new ArgumentOutOfRangeException(nameof(dailyRateBdt), "Daily rate cannot be negative");

        return new DemurrageAccrual(Guid.NewGuid(), tenantId, fileId, containerRef.Trim(), portCode.Trim(),
            landingDate, freeDays, dailyRateBdt);
    }

    /// <summary>
    /// Accrues demurrage up to <paramref name="asOfDate"/> after free time
    /// expires (BR-CUS-04). Idempotent — recomputes the full accrual.
    /// </summary>
    public void Accrue(DateOnly asOfDate)
    {
        if (asOfDate < LandingDate)
            return;

        int days = asOfDate.DayNumber - LandingDate.DayNumber - FreeDays;
        if (days <= 0)
        {
            AccruedDays = 0;
            AccruedAmountBdt = 0m;
            return;
        }

        AccruedDays = days;
        AccruedAmountBdt = Math.Round(days * DailyRateBdt, 2, MidpointRounding.ToEven);
    }
}