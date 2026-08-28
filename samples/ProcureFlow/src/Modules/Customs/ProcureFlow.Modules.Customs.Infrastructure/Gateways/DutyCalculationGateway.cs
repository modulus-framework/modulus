using Microsoft.EntityFrameworkCore;
using ProcureFlow.Modules.Customs.Domain.Duty;
using ProcureFlow.Modules.Customs.Domain.Entities;
using ProcureFlow.Modules.Customs.Infrastructure.Database;
using ProcureFlow.Shared.Application.Abstractions.Gateways;

namespace ProcureFlow.Modules.Customs.Infrastructure.Gateways;

/// <summary>
/// IDutyCalculationGateway backed by the effective-dated duty-rate registry
/// (BR-DS-01) and the deterministic cascade (§23.1, BR-AI-07). Used by
/// Procurement feasibility and Costing landed-cost estimates.
/// </summary>
public sealed class DutyCalculationGateway(CustomsDbContext context) : IDutyCalculationGateway
{
    public async Task<DutyEstimateResult> EstimateAsync(DutyEstimateRequest request, CancellationToken ct = default)
    {
        IReadOnlyDictionary<DutyComponent, DutyRateRow> rates = await GetEffectiveRatesAsync(request.HsCode, request.AssessmentDate, ct);
        IReadOnlyList<SroBenefitApplication> sro = await GetActiveSroBenefitsAsync(request.HsCode, request.TenantId, request.AssessmentDate, ct);

        DutyCalculationResult calc = DutyCascadeCalculator.Calculate(
            request.Quantity,
            request.UnitPrice,
            request.FreightShare,
            request.InsuranceShare,
            request.ExchangeRateToBdt,
            DutyCascadeCalculator.DefaultLandingChargePct,
            null,
            rates,
            sro);

        return new DutyEstimateResult(
            calc.AvEffective,
            calc.Tti,
            calc.Components.Select(c => new DutyComponentEstimate(
                c.Component.ToString(),
                c.RateDescription,
                c.Amount)).ToList());
    }

    private async Task<IReadOnlyDictionary<DutyComponent, DutyRateRow>> GetEffectiveRatesAsync(string hsCode, DateOnly date, CancellationToken ct)
    {
        var rates = await context.DutyRates
            .Where(d => d.HsCode == hsCode && d.Status == DutyRateStatus.Approved && d.IsEffectiveOn(date))
            .AsNoTracking()
            .ToListAsync(ct);

        return rates.ToDictionary(
            d => d.Component,
            d => new DutyRateRow(d.Id, d.Component, d.Rate, d.SpecificRate, d.Uom, d.EffectiveFrom, d.EffectiveTo));
    }

    private async Task<IReadOnlyList<SroBenefitApplication>> GetActiveSroBenefitsAsync(string hsCode, Guid tenantId, DateOnly date, CancellationToken ct)
    {
        var benefits = await context.SroBenefits
            .Where(s => s.IsEffectiveOn(date))
            .AsNoTracking()
            .ToListAsync(ct);

        return benefits
            .Where(s => s.AppliesTo(hsCode, tenantId))
            .Select(s => new SroBenefitApplication(s.Id, s.Name, s.Type, s.OverrideRate, s.CapPercent))
            .ToList();
    }
}