using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using ProcureFlow.Modules.Import.Application.Commands;
using ProcureFlow.Modules.Import.Application.Dtos;
using ProcureFlow.Modules.Import.Domain.Entities;
using ProcureFlow.Modules.Import.Domain.Repositories;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Import.Application.Commands;

public sealed class CreateImportFileHandler(
    IImportFileRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser) : ICommandHandler<CreateImportFileCommand, Result<ImportFileResponse>>
{
    public async Task<Result<ImportFileResponse>> HandleAsync(CreateImportFileCommand request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        int sequence = await repository.NextSequenceAsync(tenantId, request.CompanyId, request.FiscalYear, ct);
        var file = ImportFile.Create(tenantId, request.CompanyId, request.FiscalYear, sequence, request.PoId,
            request.Incoterm, request.Currency, request.PortOfLoading, request.PortOfDischarge,
            request.EstimatedGoodsValue, currentUser.UserName ?? "system");

        await repository.AddAsync(file, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ImportResponseFactory.ToFileResponse(file));
    }
}

public sealed class LinkImportPoHandler(
    IImportFileRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<LinkImportPoCommand, Result<ImportFileResponse>>
{
    public async Task<Result<ImportFileResponse>> HandleAsync(LinkImportPoCommand request, CancellationToken ct)
    {
        ImportFile? file = await repository.GetByIdAsync(request.FileId, ct);
        if (file is null)
            return Result.Failure<ImportFileResponse>(Error.NotFound("Import.NotFound", "Import file not found"));

        Result result = file.LinkPo(request.PoId);
        if (result.IsFailure)
            return Result.Failure<ImportFileResponse>(result.Error);

        await repository.SaveAsync(file, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ImportResponseFactory.ToFileResponse(file));
    }
}

public sealed class AcceptPiHandler(
    IImportFileRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<AcceptPiCommand, Result<ImportFileResponse>>
{
    public async Task<Result<ImportFileResponse>> HandleAsync(AcceptPiCommand request, CancellationToken ct)
    {
        ImportFile? file = await repository.GetByIdAsync(request.FileId, ct);
        if (file is null)
            return Result.Failure<ImportFileResponse>(Error.NotFound("Import.NotFound", "Import file not found"));

        Result result = file.AcceptPi(request.PiId);
        if (result.IsFailure)
            return Result.Failure<ImportFileResponse>(result.Error);

        await repository.SaveAsync(file, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ImportResponseFactory.ToFileResponse(file));
    }
}

public sealed class InstrumentFileHandler(
    IImportFileRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<InstrumentFileCommand, Result<ImportFileResponse>>
{
    public async Task<Result<ImportFileResponse>> HandleAsync(InstrumentFileCommand request, CancellationToken ct)
    {
        ImportFile? file = await repository.GetByIdAsync(request.FileId, ct);
        if (file is null)
            return Result.Failure<ImportFileResponse>(Error.NotFound("Import.NotFound", "Import file not found"));

        Result result = file.Instrument(request.LcId, request.TtId);
        if (result.IsFailure)
            return Result.Failure<ImportFileResponse>(result.Error);

        await repository.SaveAsync(file, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ImportResponseFactory.ToFileResponse(file));
    }
}

public sealed class AdvanceFileHandler(
    IImportFileRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<AdvanceFileCommand, Result<ImportFileResponse>>
{
    public async Task<Result<ImportFileResponse>> HandleAsync(AdvanceFileCommand request, CancellationToken ct)
    {
        ImportFile? file = await repository.GetByIdAsync(request.FileId, ct);
        if (file is null)
            return Result.Failure<ImportFileResponse>(Error.NotFound("Import.NotFound", "Import file not found"));

        Result result = request.ToStatus switch
        {
            ImportFileStatus.Shipped => file.MarkShipped(request.ShipmentId ?? Guid.Empty),
            ImportFileStatus.DocumentsInBank => file.PresentToBank(),
            ImportFileStatus.DocumentsReleased => file.ReleaseDocuments(),
            ImportFileStatus.AtPort => file.ArriveAtPort(request.LandingDate ?? DateOnly.FromDateTime(DateTime.UtcNow)),
            ImportFileStatus.UnderAssessment => file.UnderAssessment(),
            ImportFileStatus.DutyPaid => file.MarkDutyPaid(request.BoeId ?? Guid.Empty),
            ImportFileStatus.Released => file.Release(),
            ImportFileStatus.InTransitInland => file.DispatchInland(),
            ImportFileStatus.Received => file.Receive(),
            ImportFileStatus.Costed => file.FinalizeCost(),
            ImportFileStatus.Closed => file.Close(),
            _ => Result.Failure(Error.BusinessRule("Import.UnsupportedTransition",
                $"Transition to {request.ToStatus} is not supported by this endpoint"))
        };

        if (result.IsFailure)
            return Result.Failure<ImportFileResponse>(result.Error);

        await repository.SaveAsync(file, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ImportResponseFactory.ToFileResponse(file));
    }
}

public sealed class AssignCnfAgentHandler(
    IImportFileRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<AssignCnfAgentCommand, Result<ImportFileResponse>>
{
    public async Task<Result<ImportFileResponse>> HandleAsync(AssignCnfAgentCommand request, CancellationToken ct)
    {
        ImportFile? file = await repository.GetByIdAsync(request.FileId, ct);
        if (file is null)
            return Result.Failure<ImportFileResponse>(Error.NotFound("Import.NotFound", "Import file not found"));

        Result result = file.AssignCnfAgent(request.CnfAgentId);
        if (result.IsFailure)
            return Result.Failure<ImportFileResponse>(result.Error);

        await repository.SaveAsync(file, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ImportResponseFactory.ToFileResponse(file));
    }
}

public sealed class HoldFileHandler(
    IImportFileRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<HoldFileCommand, Result<ImportFileResponse>>
{
    public async Task<Result<ImportFileResponse>> HandleAsync(HoldFileCommand request, CancellationToken ct)
    {
        ImportFile? file = await repository.GetByIdAsync(request.FileId, ct);
        if (file is null)
            return Result.Failure<ImportFileResponse>(Error.NotFound("Import.NotFound", "Import file not found"));

        Result result = file.Hold(request.Reason);
        if (result.IsFailure)
            return Result.Failure<ImportFileResponse>(result.Error);

        await repository.SaveAsync(file, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ImportResponseFactory.ToFileResponse(file));
    }
}

public sealed class ResumeFileHandler(
    IImportFileRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<ResumeFileCommand, Result<ImportFileResponse>>
{
    public async Task<Result<ImportFileResponse>> HandleAsync(ResumeFileCommand request, CancellationToken ct)
    {
        ImportFile? file = await repository.GetByIdAsync(request.FileId, ct);
        if (file is null)
            return Result.Failure<ImportFileResponse>(Error.NotFound("Import.NotFound", "Import file not found"));

        Result result = file.Resume();
        if (result.IsFailure)
            return Result.Failure<ImportFileResponse>(result.Error);

        await repository.SaveAsync(file, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ImportResponseFactory.ToFileResponse(file));
    }
}

public sealed class MarkDisputedHandler(
    IImportFileRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<MarkDisputedCommand, Result<ImportFileResponse>>
{
    public async Task<Result<ImportFileResponse>> HandleAsync(MarkDisputedCommand request, CancellationToken ct)
    {
        ImportFile? file = await repository.GetByIdAsync(request.FileId, ct);
        if (file is null)
            return Result.Failure<ImportFileResponse>(Error.NotFound("Import.NotFound", "Import file not found"));

        Result result = file.MarkDisputed(request.Reason);
        if (result.IsFailure)
            return Result.Failure<ImportFileResponse>(result.Error);

        await repository.SaveAsync(file, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ImportResponseFactory.ToFileResponse(file));
    }
}

public sealed class CancelFileHandler(
    IImportFileRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<CancelFileCommand, Result<ImportFileResponse>>
{
    public async Task<Result<ImportFileResponse>> HandleAsync(CancelFileCommand request, CancellationToken ct)
    {
        ImportFile? file = await repository.GetByIdAsync(request.FileId, ct);
        if (file is null)
            return Result.Failure<ImportFileResponse>(Error.NotFound("Import.NotFound", "Import file not found"));

        Result result = file.Cancel(request.Reason);
        if (result.IsFailure)
            return Result.Failure<ImportFileResponse>(result.Error);

        await repository.SaveAsync(file, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ImportResponseFactory.ToFileResponse(file));
    }
}

public sealed class AddFileCostEntryHandler(
    IImportFileRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<AddFileCostEntryCommand, Result<ImportFileResponse>>
{
    public async Task<Result<ImportFileResponse>> HandleAsync(AddFileCostEntryCommand request, CancellationToken ct)
    {
        ImportFile? file = await repository.GetByIdAsync(request.FileId, ct);
        if (file is null)
            return Result.Failure<ImportFileResponse>(Error.NotFound("Import.NotFound", "Import file not found"));

        file.AddCostEntry(new ImportCostEntry(Guid.NewGuid(), file.Id, request.Element, request.AmountFcy,
            request.AmountBdt, request.Currency, request.SourceDocType, request.SourceDocId,
            request.SourceDocNumber, request.Direction));

        await repository.SaveAsync(file, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ImportResponseFactory.ToFileResponse(file));
    }
}

public sealed class RegisterFileDocumentHandler(
    IImportFileRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<RegisterFileDocumentCommand, Result<ImportFileResponse>>
{
    public async Task<Result<ImportFileResponse>> HandleAsync(RegisterFileDocumentCommand request, CancellationToken ct)
    {
        ImportFile? file = await repository.GetByIdAsync(request.FileId, ct);
        if (file is null)
            return Result.Failure<ImportFileResponse>(Error.NotFound("Import.NotFound", "Import file not found"));

        file.RegisterDocument(new FileDocument(Guid.NewGuid(), file.Id, request.Type, request.Name,
            request.IsMandatory, request.IsPresent));

        await repository.SaveAsync(file, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ImportResponseFactory.ToFileResponse(file));
    }
}

public sealed class RegisterContainerHandler(
    IImportFileRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<RegisterContainerCommand, Result<ImportFileResponse>>
{
    public async Task<Result<ImportFileResponse>> HandleAsync(RegisterContainerCommand request, CancellationToken ct)
    {
        ImportFile? file = await repository.GetByIdAsync(request.FileId, ct);
        if (file is null)
            return Result.Failure<ImportFileResponse>(Error.NotFound("Import.NotFound", "Import file not found"));

        try
        {
            var container = new ImportContainer(Guid.NewGuid(), file.Id, request.ContainerNo, request.SizeType,
                request.IsoCode, request.SealNo);
            file.AddContainer(container);
        }
        catch (ArgumentException ex)
        {
            return Result.Failure<ImportFileResponse>(Error.Validation("Import.Container", ex.Message));
        }

        await repository.SaveAsync(file, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ImportResponseFactory.ToFileResponse(file));
    }
}

public sealed class LandContainerHandler(
    IImportFileRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<LandContainerCommand, Result<ImportFileResponse>>
{
    public async Task<Result<ImportFileResponse>> HandleAsync(LandContainerCommand request, CancellationToken ct)
    {
        ImportFile? file = await repository.GetByIdAsync(request.FileId, ct);
        if (file is null)
            return Result.Failure<ImportFileResponse>(Error.NotFound("Import.NotFound", "Import file not found"));

        ImportContainer? container = file.Containers.FirstOrDefault(c => c.Id == request.ContainerId);
        if (container is null)
            return Result.Failure<ImportFileResponse>(Error.NotFound("Import.Container.NotFound", "Container not found on file"));

        container.Land(request.LandingDate, file.DemurrageFreeDays);
        await repository.SaveAsync(file, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ImportResponseFactory.ToFileResponse(file));
    }
}

public sealed class CreateProformaInvoiceHandler(
    IProformaInvoiceRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser) : ICommandHandler<CreateProformaInvoiceCommand, Result<ProformaInvoiceResponse>>
{
    public async Task<Result<ProformaInvoiceResponse>> HandleAsync(CreateProformaInvoiceCommand request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        var pi = ProformaInvoice.Create(tenantId, request.FileId, request.PoId, request.PiNumber, request.Currency,
            request.BeneficiaryName, request.BeneficiaryBank, request.BeneficiaryAccount, request.IssuedOn,
            request.ValidUntil, currentUser.UserName ?? "system");

        foreach (ProFormaLineInput line in request.Lines)
        {
            pi.AddLine(new ProformaInvoiceLine(Guid.NewGuid(), pi.Id, line.PoLineId, null, line.Description,
                line.Quantity, line.Uom, line.UnitPrice));
        }

        await repository.AddAsync(pi, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ImportResponseFactory.ToPiResponse(pi));
    }
}

public sealed class ReconcilePiToPoHandler(
    IProformaInvoiceRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<ReconcilePiToPoCommand, Result<ProformaInvoiceResponse>>
{
    public async Task<Result<ProformaInvoiceResponse>> HandleAsync(ReconcilePiToPoCommand request, CancellationToken ct)
    {
        ProformaInvoice? pi = await repository.GetByIdAsync(request.PiId, ct);
        if (pi is null)
            return Result.Failure<ProformaInvoiceResponse>(Error.NotFound("Pi.NotFound", "PI not found"));

        Result result = pi.ReconcileToPo(request.PoLineId, request.PoQuantity, request.PoUnitPrice, request.TolerancePct);
        if (result.IsFailure)
            return Result.Failure<ProformaInvoiceResponse>(result.Error);

        await repository.SaveAsync(pi, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ImportResponseFactory.ToPiResponse(pi));
    }
}

public sealed class AcceptPiForLcHandler(
    IProformaInvoiceRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<AcceptPiForLcCommand, Result<ProformaInvoiceResponse>>
{
    public async Task<Result<ProformaInvoiceResponse>> HandleAsync(AcceptPiForLcCommand request, CancellationToken ct)
    {
        ProformaInvoice? pi = await repository.GetByIdAsync(request.PiId, ct);
        if (pi is null)
            return Result.Failure<ProformaInvoiceResponse>(Error.NotFound("Pi.NotFound", "PI not found"));

        Result result = pi.AcceptForLc();
        if (result.IsFailure)
            return Result.Failure<ProformaInvoiceResponse>(result.Error);

        await repository.SaveAsync(pi, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ImportResponseFactory.ToPiResponse(pi));
    }
}

public sealed class CreateCommercialInvoiceHandler(
    ICommercialInvoiceRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser) : ICommandHandler<CreateCommercialInvoiceCommand, Result<CommercialInvoiceResponse>>
{
    public async Task<Result<CommercialInvoiceResponse>> HandleAsync(CreateCommercialInvoiceCommand request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        var ci = CommercialInvoice.Create(tenantId, request.FileId, request.PiId, request.CiNumber, request.Currency,
            request.TotalFcy, request.IssuedOn, currentUser.UserName ?? "system");

        foreach (CommercialLineInput line in request.Lines)
        {
            ci.AddLine(new CommercialInvoiceLine(Guid.NewGuid(), ci.Id, line.PiLineId, line.Description,
                line.Quantity, line.Uom, line.UnitPrice));
        }

        await repository.AddAsync(ci, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ImportResponseFactory.ToCiResponse(ci));
    }
}

public sealed class ReconcileCiToPiHandler(
    ICommercialInvoiceRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<ReconcileCiToPiCommand, Result<CommercialInvoiceResponse>>
{
    public async Task<Result<CommercialInvoiceResponse>> HandleAsync(ReconcileCiToPiCommand request, CancellationToken ct)
    {
        CommercialInvoice? ci = await repository.GetByIdAsync(request.CiId, ct);
        if (ci is null)
            return Result.Failure<CommercialInvoiceResponse>(Error.NotFound("Ci.NotFound", "CI not found"));

        Result result = ci.ReconcileToPi(request.PiTotal, request.TolerancePct);
        if (result.IsFailure)
            return Result.Failure<CommercialInvoiceResponse>(result.Error);

        await repository.SaveAsync(ci, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ImportResponseFactory.ToCiResponse(ci));
    }
}

public sealed class CreateShipmentHandler(
    IShipmentRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser) : ICommandHandler<CreateShipmentCommand, Result<ShipmentResponse>>
{
    public async Task<Result<ShipmentResponse>> HandleAsync(CreateShipmentCommand request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        var shipment = Shipment.Create(tenantId, request.FileId, request.CiId, request.ShipmentNo, request.Mode,
            request.VesselVoyage, request.Etd, request.Eta, currentUser.UserName ?? "system");

        await repository.AddAsync(shipment, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ImportResponseFactory.ToShipmentResponse(shipment));
    }
}

public sealed class RecordEtaChangeHandler(
    IShipmentRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<RecordEtaChangeCommand, Result<ShipmentResponse>>
{
    public async Task<Result<ShipmentResponse>> HandleAsync(RecordEtaChangeCommand request, CancellationToken ct)
    {
        Shipment? shipment = await repository.GetByIdAsync(request.ShipmentId, ct);
        if (shipment is null)
            return Result.Failure<ShipmentResponse>(Error.NotFound("Shipment.NotFound", "Shipment not found"));

        Result result = shipment.RecordEtaChange(request.NewEta);
        if (result.IsFailure)
            return Result.Failure<ShipmentResponse>(result.Error);

        await repository.SaveAsync(shipment, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ImportResponseFactory.ToShipmentResponse(shipment));
    }
}

public sealed class CheckLcBreachRiskHandler(
    IShipmentRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<CheckLcBreachRiskCommand, Result<ShipmentResponse>>
{
    public async Task<Result<ShipmentResponse>> HandleAsync(CheckLcBreachRiskCommand request, CancellationToken ct)
    {
        Shipment? shipment = await repository.GetByIdAsync(request.ShipmentId, ct);
        if (shipment is null)
            return Result.Failure<ShipmentResponse>(Error.NotFound("Shipment.NotFound", "Shipment not found"));

        if (shipment.IsLcBreachRisk(request.LatestShipmentDate, DateOnly.FromDateTime(DateTime.UtcNow)))
            shipment.AlertLcBreachRisk();

        await repository.SaveAsync(shipment, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ImportResponseFactory.ToShipmentResponse(shipment));
    }
}

public sealed class CreateCnfAgentHandler(
    ICnfAgentRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant) : ICommandHandler<CreateCnfAgentCommand, Result<CnfAgentResponse>>
{
    public async Task<Result<CnfAgentResponse>> HandleAsync(CreateCnfAgentCommand request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        var agent = CnfAgent.Create(tenantId, request.Name, request.AinNumber, request.Contacts);

        await repository.AddAsync(agent, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ImportResponseFactory.ToAgentResponse(agent));
    }
}

public sealed class SetCnfRateCardHandler(
    ICnfAgentRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<SetCnfRateCardCommand, Result<CnfAgentResponse>>
{
    public async Task<Result<CnfAgentResponse>> HandleAsync(SetCnfRateCardCommand request, CancellationToken ct)
    {
        CnfAgent? agent = await repository.GetByIdAsync(request.AgentId, ct);
        if (agent is null)
            return Result.Failure<CnfAgentResponse>(Error.NotFound("Cnf.NotFound", "C&F agent not found"));

        agent.SetRateCard(request.PerBoe, request.PerContainer, request.PctOfValue, request.DocumentationCharges);
        await repository.SaveAsync(agent, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ImportResponseFactory.ToAgentResponse(agent));
    }
}

// ── Packing List Handlers (BR-DOC-06) ───────────────────────────────

public sealed class CreatePackingListHandler(
    IPackingListRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant) : ICommandHandler<CreatePackingListCommand, Result<PackingListResponse>>
{
    public async Task<Result<PackingListResponse>> HandleAsync(CreatePackingListCommand request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        var pl = PackingList.Create(tenantId, request.FileId, request.CiId, request.PlNumber,
            request.Cartons, request.NetWeightKg, request.GrossWeightKg, request.VolumeCbm);

        foreach (var line in request.Lines)
        {
            pl.AddLine(new PackingListLine(Guid.NewGuid(), pl.Id, line.CiLineId,
                line.Quantity, line.Uom, line.NetWeightKg, line.GrossWeightKg, line.VolumeCbm));
        }

        await repository.AddAsync(pl, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ImportResponseFactory.ToPackingListResponse(pl));
    }
}

public sealed class ValidatePackingListHandler(
    IPackingListRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<ValidatePackingListCommand, Result<PackingListResponse>>
{
    public async Task<Result<PackingListResponse>> HandleAsync(ValidatePackingListCommand request, CancellationToken ct)
    {
        PackingList? pl = await repository.GetByIdAsync(request.PlId, ct);
        if (pl is null)
            return Result.Failure<PackingListResponse>(Error.NotFound("PackingList.NotFound", "Packing list not found"));

        Result result = pl.ValidateAgainstCi(request.CiQuantity, request.TolerancePct);
        if (result.IsFailure)
            return Result.Failure<PackingListResponse>(result.Error);

        await repository.SaveAsync(pl, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ImportResponseFactory.ToPackingListResponse(pl));
    }
}

// ── Import Permit Handlers (BR-PM-01/02) ────────────────────────────

public sealed class CreateImportPermitHandler(
    IImportPermitRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant) : ICommandHandler<CreateImportPermitCommand, Result<ImportPermitResponse>>
{
    public async Task<Result<ImportPermitResponse>> HandleAsync(CreateImportPermitCommand request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        var permit = new ImportPermit(Guid.NewGuid(), tenantId, Guid.Empty, request.PermitNo, request.Category,
            request.CeilingQty, request.CeilingValue, request.IssuedOn, request.ExpiresOn, request.IssuedBy);

        await repository.AddAsync(permit, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ImportResponseFactory.ToPermitResponse(permit));
    }
}

public sealed class DrawPermitHandler(
    IImportPermitRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<DrawPermitCommand, Result<ImportPermitResponse>>
{
    public async Task<Result<ImportPermitResponse>> HandleAsync(DrawPermitCommand request, CancellationToken ct)
    {
        ImportPermit? permit = await repository.GetByIdAsync(request.PermitId, ct);
        if (permit is null)
            return Result.Failure<ImportPermitResponse>(Error.NotFound("Permit.NotFound", "Import permit not found"));

        Result result = permit.Draw(request.FileId, request.Qty, request.Value, DateOnly.FromDateTime(DateTime.UtcNow));
        if (result.IsFailure)
            return Result.Failure<ImportPermitResponse>(result.Error);

        await repository.SaveAsync(permit, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ImportResponseFactory.ToPermitResponse(permit));
    }
}

// ── Insurance Policy Handlers (BR-INS-01) ───────────────────────────

public sealed class CreateInsurancePolicyHandler(
    IInsurancePolicyRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant) : ICommandHandler<CreateInsurancePolicyCommand, Result<InsurancePolicyResponse>>
{
    public async Task<Result<InsurancePolicyResponse>> HandleAsync(CreateInsurancePolicyCommand request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        var policy = InsurancePolicy.Create(tenantId, request.FileId, null, request.PolicyNo,
            request.Insurer, request.CoverNoteRef, request.InsuredValueFcy,
            request.PremiumFcy, request.Currency, request.CoverStart);

        await repository.AddAsync(policy, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ImportResponseFactory.ToInsuranceResponse(policy));
    }
}

// ── Transport Document Handlers (BR-BL-01..03) ─────────────────────

public sealed class CreateTransportDocumentHandler(
    ITransportDocumentRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant) : ICommandHandler<CreateTransportDocumentCommand, Result<TransportDocumentResponse>>
{
    public async Task<Result<TransportDocumentResponse>> HandleAsync(CreateTransportDocumentCommand request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        var document = TransportDocument.Create(
            tenantId, request.ShipmentId, request.FileId, request.Type,
            request.DocumentNumber, request.IssueDate, request.OnBoardDate,
            request.FreightTerms, request.Consignee, request.NotifyParty,
            request.OriginalCount, request.SurrenderStatus);

        await repository.AddAsync(document, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ImportResponseFactory.ToTransportDocumentResponse(document));
    }
}

public sealed class TransferTransportDocumentHandler(
    ITransportDocumentRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<TransferTransportDocumentCommand, Result<TransportDocumentResponse>>
{
    public async Task<Result<TransportDocumentResponse>> HandleAsync(TransferTransportDocumentCommand request, CancellationToken ct)
    {
        TransportDocument? document = await repository.GetByIdAsync(request.DocumentId, ct);
        if (document is null)
            return Result.Failure<TransportDocumentResponse>(Error.NotFound("TransportDoc.NotFound", "Transport document not found"));

        Result result = document.TransferTo(request.NewHolder);
        if (result.IsFailure)
            return Result.Failure<TransportDocumentResponse>(result.Error);

        await repository.SaveAsync(document, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ImportResponseFactory.ToTransportDocumentResponse(document));
    }
}

// ── Freight Cost Handlers (BR-FR-01/02) ────────────────────────────

public sealed class CreateFreightCostHandler(
    IFreightCostRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant) : ICommandHandler<CreateFreightCostCommand, Result<FreightCostResponse>>
{
    public async Task<Result<FreightCostResponse>> HandleAsync(CreateFreightCostCommand request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        var cost = FreightCost.Create(
            tenantId, request.ShipmentId, request.FileId, request.CostType,
            request.Description, request.Amount, request.Currency, request.SurchargeType);

        await repository.AddAsync(cost, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ImportResponseFactory.ToFreightCostResponse(cost));
    }
}

public sealed class CommitFreightCostToActualHandler(
    IFreightCostRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<CommitFreightCostToActualCommand, Result<FreightCostResponse>>
{
    public async Task<Result<FreightCostResponse>> HandleAsync(CommitFreightCostToActualCommand request, CancellationToken ct)
    {
        FreightCost? cost = await repository.GetByIdAsync(request.FreightCostId, ct);
        if (cost is null)
            return Result.Failure<FreightCostResponse>(Error.NotFound("FreightCost.NotFound", "Freight cost not found"));

        Result result = cost.CommitToActual(request.InvoiceNo, request.InvoiceDate);
        if (result.IsFailure)
            return Result.Failure<FreightCostResponse>(result.Error);

        await unitOfWork.CommitAsync(ct);
        return Result.Success(ImportResponseFactory.ToFreightCostResponse(cost));
    }
}

// ── Bill of Entry Handlers (BR-CC-01..05) ──────────────────────────

public sealed class CreateBillOfEntryHandler(
    IBillOfEntryRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant) : ICommandHandler<CreateBillOfEntryCommand, Result<BillOfEntryResponse>>
{
    public async Task<Result<BillOfEntryResponse>> HandleAsync(CreateBillOfEntryCommand request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        var boe = BillOfEntry.Create(tenantId, request.FileId, request.BoeNumber, request.BoeDate,
            request.CustomsOffice, request.CnfAgentId, request.Lane, request.DeclarantAin);

        foreach (BoeLineInput line in request.Lines)
            boe.AddLine(line.CiLineId, line.HsCode, line.AssessableValue, line.Quantity, line.Uom);

        await repository.AddAsync(boe, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ImportResponseFactory.ToBoeResponse(boe));
    }
}

public sealed class SubmitBillOfEntryHandler(
    IBillOfEntryRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<SubmitBillOfEntryCommand, Result<BillOfEntryResponse>>
{
    public async Task<Result<BillOfEntryResponse>> HandleAsync(SubmitBillOfEntryCommand request, CancellationToken ct)
    {
        BillOfEntry? boe = await repository.GetByIdAsync(request.BoeId, ct);
        if (boe is null)
            return Result.Failure<BillOfEntryResponse>(Error.NotFound("BoE.NotFound", "Bill of Entry not found"));

        Result result = boe.Submit();
        if (result.IsFailure)
            return Result.Failure<BillOfEntryResponse>(result.Error);

        await repository.SaveAsync(boe, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ImportResponseFactory.ToBoeResponse(boe));
    }
}

public sealed class RecordBoeQueryHandler(
    IBillOfEntryRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<RecordBoeQueryCommand, Result<BillOfEntryResponse>>
{
    public async Task<Result<BillOfEntryResponse>> HandleAsync(RecordBoeQueryCommand request, CancellationToken ct)
    {
        BillOfEntry? boe = await repository.GetByIdAsync(request.BoeId, ct);
        if (boe is null)
            return Result.Failure<BillOfEntryResponse>(Error.NotFound("BoE.NotFound", "Bill of Entry not found"));

        Result result = boe.RecordQuery(request.Reason);
        if (result.IsFailure)
            return Result.Failure<BillOfEntryResponse>(result.Error);

        await repository.SaveAsync(boe, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ImportResponseFactory.ToBoeResponse(boe));
    }
}

public sealed class RecordBoeAssessmentHandler(
    IBillOfEntryRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<RecordBoeAssessmentCommand, Result<BillOfEntryResponse>>
{
    public async Task<Result<BillOfEntryResponse>> HandleAsync(RecordBoeAssessmentCommand request, CancellationToken ct)
    {
        BillOfEntry? boe = await repository.GetByIdAsync(request.BoeId, ct);
        if (boe is null)
            return Result.Failure<BillOfEntryResponse>(Error.NotFound("BoE.NotFound", "Bill of Entry not found"));

        Result result = boe.RecordAssessment();
        if (result.IsFailure)
            return Result.Failure<BillOfEntryResponse>(result.Error);

        await repository.SaveAsync(boe, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ImportResponseFactory.ToBoeResponse(boe));
    }
}

public sealed class RecordBoePaymentHandler(
    IBillOfEntryRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<RecordBoePaymentCommand, Result<BillOfEntryResponse>>
{
    public async Task<Result<BillOfEntryResponse>> HandleAsync(RecordBoePaymentCommand request, CancellationToken ct)
    {
        BillOfEntry? boe = await repository.GetByIdAsync(request.BoeId, ct);
        if (boe is null)
            return Result.Failure<BillOfEntryResponse>(Error.NotFound("BoE.NotFound", "Bill of Entry not found"));

        Result result = boe.RecordPayment();
        if (result.IsFailure)
            return Result.Failure<BillOfEntryResponse>(result.Error);

        await repository.SaveAsync(boe, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ImportResponseFactory.ToBoeResponse(boe));
    }
}

public sealed class RecordBoeExaminationHandler(
    IBillOfEntryRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<RecordBoeExaminationCommand, Result<BillOfEntryResponse>>
{
    public async Task<Result<BillOfEntryResponse>> HandleAsync(RecordBoeExaminationCommand request, CancellationToken ct)
    {
        BillOfEntry? boe = await repository.GetByIdAsync(request.BoeId, ct);
        if (boe is null)
            return Result.Failure<BillOfEntryResponse>(Error.NotFound("BoE.NotFound", "Bill of Entry not found"));

        Result result = boe.RecordExamination(request.Lane);
        if (result.IsFailure)
            return Result.Failure<BillOfEntryResponse>(result.Error);

        await repository.SaveAsync(boe, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ImportResponseFactory.ToBoeResponse(boe));
    }
}

public sealed class ReleaseBillOfEntryHandler(
    IBillOfEntryRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<ReleaseBillOfEntryCommand, Result<BillOfEntryResponse>>
{
    public async Task<Result<BillOfEntryResponse>> HandleAsync(ReleaseBillOfEntryCommand request, CancellationToken ct)
    {
        BillOfEntry? boe = await repository.GetByIdAsync(request.BoeId, ct);
        if (boe is null)
            return Result.Failure<BillOfEntryResponse>(Error.NotFound("BoE.NotFound", "Bill of Entry not found"));

        Result result = boe.Release();
        if (result.IsFailure)
            return Result.Failure<BillOfEntryResponse>(result.Error);

        await repository.SaveAsync(boe, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ImportResponseFactory.ToBoeResponse(boe));
    }
}

public sealed class AddBoeDutyLineHandler(
    IBillOfEntryRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<AddBoeDutyLineCommand, Result<BillOfEntryResponse>>
{
    public async Task<Result<BillOfEntryResponse>> HandleAsync(AddBoeDutyLineCommand request, CancellationToken ct)
    {
        BillOfEntry? boe = await repository.GetByIdAsync(request.BoeId, ct);
        if (boe is null)
            return Result.Failure<BillOfEntryResponse>(Error.NotFound("BoE.NotFound", "Bill of Entry not found"));

        boe.AddDutyLine(request.Component, request.Rate, request.Amount, request.SroRef);

        await repository.SaveAsync(boe, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ImportResponseFactory.ToBoeResponse(boe));
    }
}

// ── Assessment Variance Handlers (BR-CC-03) ────────────────────────

public sealed class CreateAssessmentVarianceHandler(
    IAssessmentVarianceRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant) : ICommandHandler<CreateAssessmentVarianceCommand, Result<AssessmentVarianceResponse>>
{
    public async Task<Result<AssessmentVarianceResponse>> HandleAsync(CreateAssessmentVarianceCommand request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        var variance = AssessmentVariance.Create(tenantId, request.BoeId, request.BoeLineId,
            request.Type, request.Component, request.SystemAmount, request.AssessedAmount, request.Reason);

        await repository.AddAsync(variance, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ImportResponseFactory.ToVarianceResponse(variance));
    }
}

public sealed class ResolveAssessmentVarianceHandler(
    IAssessmentVarianceRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<ResolveAssessmentVarianceCommand, Result<AssessmentVarianceResponse>>
{
    public async Task<Result<AssessmentVarianceResponse>> HandleAsync(ResolveAssessmentVarianceCommand request, CancellationToken ct)
    {
        AssessmentVariance? variance = await repository.GetByIdAsync(request.VarianceId, ct);
        if (variance is null)
            return Result.Failure<AssessmentVarianceResponse>(Error.NotFound("Variance.NotFound", "Assessment variance not found"));

        Result result = variance.Resolve(request.Resolution);
        if (result.IsFailure)
            return Result.Failure<AssessmentVarianceResponse>(result.Error);

        await unitOfWork.CommitAsync(ct);
        return Result.Success(ImportResponseFactory.ToVarianceResponse(variance));
    }
}

public sealed class AcceptAssessmentVarianceHandler(
    IAssessmentVarianceRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<AcceptAssessmentVarianceCommand, Result<AssessmentVarianceResponse>>
{
    public async Task<Result<AssessmentVarianceResponse>> HandleAsync(AcceptAssessmentVarianceCommand request, CancellationToken ct)
    {
        AssessmentVariance? variance = await repository.GetByIdAsync(request.VarianceId, ct);
        if (variance is null)
            return Result.Failure<AssessmentVarianceResponse>(Error.NotFound("Variance.NotFound", "Assessment variance not found"));

        Result result = variance.Accept();
        if (result.IsFailure)
            return Result.Failure<AssessmentVarianceResponse>(result.Error);

        await unitOfWork.CommitAsync(ct);
        return Result.Success(ImportResponseFactory.ToVarianceResponse(variance));
    }
}

// ── Port Charge Handlers (BR-CC-04) ────────────────────────────────

public sealed class CreatePortChargeHandler(
    IPortChargeRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant) : ICommandHandler<CreatePortChargeCommand, Result<PortChargeResponse>>
{
    public async Task<Result<PortChargeResponse>> HandleAsync(CreatePortChargeCommand request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        var charge = PortCharge.Create(tenantId, request.FileId, request.ChargeType,
            request.Amount, request.Currency, request.ChargedOn, request.Description);

        await repository.AddAsync(charge, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ImportResponseFactory.ToPortChargeResponse(charge));
    }
}

// ── Import Plan Handlers (BR-IP-01..06) ─────────────────────────────

public sealed class CreateImportPlanHandler(
    IImportPlanRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant) : ICommandHandler<CreateImportPlanCommand, Result<ImportPlanResponse>>
{
    public async Task<Result<ImportPlanResponse>> HandleAsync(CreateImportPlanCommand request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        var plan = ImportPlan.Create(tenantId, request.CompanyId, request.FiscalYear,
            request.PeriodStart, request.PeriodEnd, request.Currency);

        foreach (ImportPlanLineInput line in request.Lines)
            plan.AddLine(line.ItemId, line.CategoryId, line.Description, line.EstQty,
                line.EstFob, line.EstLanded, line.TargetMonth, line.SourceCountry);

        await repository.AddAsync(plan, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ImportResponseFactory.ToPlanResponse(plan));
    }
}

public sealed class AddImportPlanLineHandler(
    IImportPlanRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<AddImportPlanLineCommand, Result<ImportPlanResponse>>
{
    public async Task<Result<ImportPlanResponse>> HandleAsync(AddImportPlanLineCommand request, CancellationToken ct)
    {
        ImportPlan? plan = await repository.GetByIdAsync(request.PlanId, ct);
        if (plan is null)
            return Result.Failure<ImportPlanResponse>(Error.NotFound("Plan.NotFound", "Import plan not found"));

        plan.AddLine(request.ItemId, request.CategoryId, request.Description,
            request.EstQty, request.EstFob, request.EstLanded, request.TargetMonth, request.SourceCountry);

        await repository.SaveAsync(plan, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ImportResponseFactory.ToPlanResponse(plan));
    }
}

public sealed class RemoveImportPlanLineHandler(
    IImportPlanRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<RemoveImportPlanLineCommand, Result<ImportPlanResponse>>
{
    public async Task<Result<ImportPlanResponse>> HandleAsync(RemoveImportPlanLineCommand request, CancellationToken ct)
    {
        ImportPlan? plan = await repository.GetByIdAsync(request.PlanId, ct);
        if (plan is null)
            return Result.Failure<ImportPlanResponse>(Error.NotFound("Plan.NotFound", "Import plan not found"));

        plan.RemoveLine(request.LineId);

        await repository.SaveAsync(plan, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ImportResponseFactory.ToPlanResponse(plan));
    }
}

public sealed class SubmitImportPlanHandler(
    IImportPlanRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<SubmitImportPlanCommand, Result<ImportPlanResponse>>
{
    public async Task<Result<ImportPlanResponse>> HandleAsync(SubmitImportPlanCommand request, CancellationToken ct)
    {
        ImportPlan? plan = await repository.GetByIdAsync(request.PlanId, ct);
        if (plan is null)
            return Result.Failure<ImportPlanResponse>(Error.NotFound("Plan.NotFound", "Import plan not found"));

        Result result = plan.Submit();
        if (result.IsFailure)
            return Result.Failure<ImportPlanResponse>(result.Error);

        await repository.SaveAsync(plan, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ImportResponseFactory.ToPlanResponse(plan));
    }
}

public sealed class ApproveImportPlanHandler(
    IImportPlanRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant) : ICommandHandler<ApproveImportPlanCommand, Result<ImportPlanResponse>>
{
    public async Task<Result<ImportPlanResponse>> HandleAsync(ApproveImportPlanCommand request, CancellationToken ct)
    {
        ImportPlan? plan = await repository.GetByIdAsync(request.PlanId, ct);
        if (plan is null)
            return Result.Failure<ImportPlanResponse>(Error.NotFound("Plan.NotFound", "Import plan not found"));

        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        Result result = plan.Approve(tenantId);
        if (result.IsFailure)
            return Result.Failure<ImportPlanResponse>(result.Error);

        await repository.SaveAsync(plan, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ImportResponseFactory.ToPlanResponse(plan));
    }
}

public sealed class ReviseImportPlanHandler(
    IImportPlanRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<ReviseImportPlanCommand, Result<ImportPlanResponse>>
{
    public async Task<Result<ImportPlanResponse>> HandleAsync(ReviseImportPlanCommand request, CancellationToken ct)
    {
        ImportPlan? plan = await repository.GetByIdAsync(request.PlanId, ct);
        if (plan is null)
            return Result.Failure<ImportPlanResponse>(Error.NotFound("Plan.NotFound", "Import plan not found"));

        Result result = plan.Revise();
        if (result.IsFailure)
            return Result.Failure<ImportPlanResponse>(result.Error);

        await repository.SaveAsync(plan, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ImportResponseFactory.ToPlanResponse(plan));
    }
}

public sealed class CloseImportPlanHandler(
    IImportPlanRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<CloseImportPlanCommand, Result<ImportPlanResponse>>
{
    public async Task<Result<ImportPlanResponse>> HandleAsync(CloseImportPlanCommand request, CancellationToken ct)
    {
        ImportPlan? plan = await repository.GetByIdAsync(request.PlanId, ct);
        if (plan is null)
            return Result.Failure<ImportPlanResponse>(Error.NotFound("Plan.NotFound", "Import plan not found"));

        Result result = plan.Close();
        if (result.IsFailure)
            return Result.Failure<ImportPlanResponse>(result.Error);

        await repository.SaveAsync(plan, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ImportResponseFactory.ToPlanResponse(plan));
    }
}

public sealed class RecordPlanActualsHandler(
    IImportPlanRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<RecordPlanActualsCommand, Result<ImportPlanResponse>>
{
    public async Task<Result<ImportPlanResponse>> HandleAsync(RecordPlanActualsCommand request, CancellationToken ct)
    {
        ImportPlan? plan = await repository.GetByIdAsync(request.PlanId, ct);
        if (plan is null)
            return Result.Failure<ImportPlanResponse>(Error.NotFound("Plan.NotFound", "Import plan not found"));

        ImportPlanLine? line = plan.Lines.FirstOrDefault(l => l.Id == request.LineId);
        if (line is null)
            return Result.Failure<ImportPlanResponse>(Error.NotFound("Plan.LineNotFound", "Plan line not found"));

        line.RecordActual(request.Qty, request.Fob, request.Landed);

        await repository.SaveAsync(plan, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ImportResponseFactory.ToPlanResponse(plan));
    }
}
public sealed class CreateCertificateOfOriginHandler(
    ICertificateOfOriginRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<CreateCertificateOfOriginCommand, Result<CertificateOfOriginResponse>>
{
    public async Task<Result<CertificateOfOriginResponse>> HandleAsync(CreateCertificateOfOriginCommand request, CancellationToken ct)
    {
        var coo = CertificateOfOrigin.Create(
            request.Fixture.TenantId, request.FileId, request.CiId,
            request.Type, request.OriginCountry, request.DocumentNo,
            request.IssuerName, request.IssuedOn, request.ExpiryDate);

        await repository.AddAsync(coo, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ImportResponseFactory.ToCertificateOfOriginResponse(coo));
    }
}

public sealed class CheckCooOriginMismatchHandler(
    ICertificateOfOriginRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<CheckCooOriginMismatchCommand, Result<CertificateOfOriginResponse>>
{
    public async Task<Result<CertificateOfOriginResponse>> HandleAsync(CheckCooOriginMismatchCommand request, CancellationToken ct)
    {
        var coo = await repository.GetByFileAsync(request.FileId, ct);
        if (coo is null)
            return Result.Failure<CertificateOfOriginResponse>(Error.NotFound("Coo.NotFound", "Certificate of Origin not found for this file"));

        var result = coo.CheckOriginMismatch(request.CiOriginCountry);
        await repository.SaveAsync(coo, ct);
        await unitOfWork.CommitAsync(ct);
        return result.IsFailure
            ? Result.Failure<CertificateOfOriginResponse>(result.Error)
            : Result.Success(ImportResponseFactory.ToCertificateOfOriginResponse(coo));
    }
}

public sealed class CreateCooIssuerRegistryHandler(
    ICooIssuerRegistryRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<CreateCooIssuerRegistryCommand, Result<CooIssuerRegistryResponse>>
{
    public async Task<Result<CooIssuerRegistryResponse>> HandleAsync(CreateCooIssuerRegistryCommand request, CancellationToken ct)
    {
        var registry = CooIssuerRegistry.Create(
            request.Fixture.TenantId, request.Country, request.IssuerName,
            request.LicenseNo, request.ValidFrom, request.ValidTo);

        await repository.AddAsync(registry, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ImportResponseFactory.ToCooIssuerRegistryResponse(registry));
    }
}
