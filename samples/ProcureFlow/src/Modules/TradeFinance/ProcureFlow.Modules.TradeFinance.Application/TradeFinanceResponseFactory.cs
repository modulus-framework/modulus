using ProcureFlow.Modules.TradeFinance.Application.Dtos;
using ProcureFlow.Modules.TradeFinance.Domain.Entities;

namespace ProcureFlow.Modules.TradeFinance.Application;

public static class TradeFinanceResponseFactory
{
    public static LetterOfCreditResponse ToLcResponse(LetterOfCredit lc) => new(
        lc.Id,
        lc.TenantId,
        lc.FileId,
        lc.PoId,
        lc.LcNumber,
        lc.Type,
        lc.Currency,
        lc.Amount,
        lc.TolerancePct,
        lc.ApplicantCompanyId,
        lc.BeneficiaryVendorId,
        lc.BeneficiaryName,
        lc.IssuingBankId,
        lc.LatestShipmentDate,
        lc.ExpiryDate,
        lc.Incoterm,
        lc.PortOfLoading,
        lc.PortOfDischarge,
        lc.PartialShipmentAllowed,
        lc.TransshipmentAllowed,
        lc.MarginPct,
        lc.BookingFxRate,
        lc.Status,
        lc.MarginBlocked,
        lc.RealizedFxRate,
        lc.TermViolations,
        lc.Charges.Select(c => new LcChargeResponse(c.Id, c.Type, c.Amount, c.Currency, c.RefDoc, c.AtUtc)).ToArray(),
        lc.Amendments.Select(a => new LcAmendmentResponse(a.Id, a.Version, a.ValueDelta, a.TenorIncreasing,
            a.ReasonCode, a.Reason, a.Doa, a.RequestedBy, a.Approved, a.ApprovedBy)).ToArray(),
        lc.Presentations.Select(p => new LcPresentationResponse(p.Id, p.PresentationNo, p.PresentedAtUtc,
            p.DocumentRefs, p.Status, p.Discrepancies.Select(d => new LcDiscrepancyResponse(d.Id, d.Code, d.Description)).ToArray())).ToArray(),
        lc.MarginLedger.Select(e => new MarginLedgerEntryResponse(e.Id, e.Type, e.Amount, e.Currency, e.BankId,
            e.Reason, e.BookedOn)).ToArray(),
        lc.Maturities.Select(m => new MaturityObligationResponse(m.Id, m.DueDate, m.Amount, m.Currency, m.Status)).ToArray());

    public static TtPaymentResponse ToTtResponse(TtPayment tt) => new(
        tt.Id,
        tt.TenantId,
        tt.FileId,
        tt.PoId,
        tt.TtNumber,
        tt.VendorId,
        tt.BeneficiaryName,
        tt.Currency,
        tt.Amount,
        tt.ScheduleType,
        tt.BankRef,
        tt.Status,
        tt.ValueDate,
        tt.FxRate,
        tt.Charges,
        tt.RequiresCfoApproval);
}