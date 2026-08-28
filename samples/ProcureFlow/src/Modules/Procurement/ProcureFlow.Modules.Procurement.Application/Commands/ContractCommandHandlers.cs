using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using ProcureFlow.Modules.Procurement.Application.Commands;
using ProcureFlow.Modules.Procurement.Application.Dtos;
using ProcureFlow.Modules.Procurement.Domain.Entities;
using ProcureFlow.Modules.Procurement.Domain.Repositories;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Procurement.Application.Commands;

public sealed class CreateContractCommandHandler(
    IContractRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser) : ICommandHandler<CreateContractCommand, Result<ContractResponse>>
{
    public async Task<Result<ContractResponse>> HandleAsync(CreateContractCommand request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        string userId = currentUser.UserId?.ToString() ?? "system";

        var contract = Contract.Create(
            Guid.NewGuid(), tenantId, request.ContractNumber, request.VendorId,
            request.Type, request.Currency, request.StartDate, request.EndDate,
            request.CapValue, request.Notes, userId);

        foreach (var lineInput in request.Lines)
        {
            contract.AddLine(new ContractLine(
                Guid.NewGuid(), lineInput.ItemId, lineInput.FreeText,
                lineInput.UnitPrice, lineInput.MinQuantity,
                lineInput.EscalationJson, lineInput.Notes));
        }

        await repository.AddAsync(contract, ct);
        await unitOfWork.CommitAsync(ct);

        return Result.Success(ToResponse(contract));
    }

    private static ContractResponse ToResponse(Contract c) => new(
        c.Id, c.TenantId, c.ContractNumber, c.VendorId, c.Type, c.Currency,
        c.StartDate, c.EndDate, c.CapValue, c.ConsumedValue, c.ConsumedPercent,
        c.Notes, c.Status, c.RevisionVersion, c.CreatedBy, c.CreatedAtUtc);
}

public sealed class SubmitContractCommandHandler(
    IContractRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant) : ICommandHandler<SubmitContractCommand, Result<ContractResponse>>
{
    public async Task<Result<ContractResponse>> HandleAsync(SubmitContractCommand request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        var contract = await repository.GetByIdAsync(request.ContractId, tenantId, ct);
        if (contract is null)
            return Result.Failure<ContractResponse>(ContractErrors.NotFound);

        var result = contract.Submit();
        if (result.IsFailure)
            return Result.Failure<ContractResponse>(result.Error);

        await repository.UpdateAsync(contract, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ToResponse(contract));
    }

    private static ContractResponse ToResponse(Contract c) => new(
        c.Id, c.TenantId, c.ContractNumber, c.VendorId, c.Type, c.Currency,
        c.StartDate, c.EndDate, c.CapValue, c.ConsumedValue, c.ConsumedPercent,
        c.Notes, c.Status, c.RevisionVersion, c.CreatedBy, c.CreatedAtUtc);
}

public sealed class ApproveContractCommandHandler(
    IContractRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant) : ICommandHandler<ApproveContractCommand, Result<ContractResponse>>
{
    public async Task<Result<ContractResponse>> HandleAsync(ApproveContractCommand request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        var contract = await repository.GetByIdAsync(request.ContractId, tenantId, ct);
        if (contract is null)
            return Result.Failure<ContractResponse>(ContractErrors.NotFound);

        var result = contract.Approve();
        if (result.IsFailure)
            return Result.Failure<ContractResponse>(result.Error);

        await repository.UpdateAsync(contract, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ToResponse(contract));
    }

    private static ContractResponse ToResponse(Contract c) => new(
        c.Id, c.TenantId, c.ContractNumber, c.VendorId, c.Type, c.Currency,
        c.StartDate, c.EndDate, c.CapValue, c.ConsumedValue, c.ConsumedPercent,
        c.Notes, c.Status, c.RevisionVersion, c.CreatedBy, c.CreatedAtUtc);
}

public sealed class RenewContractCommandHandler(
    IContractRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser) : ICommandHandler<RenewContractCommand, Result<ContractResponse>>
{
    public async Task<Result<ContractResponse>> HandleAsync(RenewContractCommand request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        string userId = currentUser.UserId?.ToString() ?? "system";
        var contract = await repository.GetByIdAsync(request.ContractId, tenantId, ct);
        if (contract is null)
            return Result.Failure<ContractResponse>(ContractErrors.NotFound);

        var result = contract.Renew(request.NewEndDate, request.NewCapValue, request.Reason, userId);
        if (result.IsFailure)
            return Result.Failure<ContractResponse>(result.Error);

        await repository.UpdateAsync(contract, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ToResponse(contract));
    }

    private static ContractResponse ToResponse(Contract c) => new(
        c.Id, c.TenantId, c.ContractNumber, c.VendorId, c.Type, c.Currency,
        c.StartDate, c.EndDate, c.CapValue, c.ConsumedValue, c.ConsumedPercent,
        c.Notes, c.Status, c.RevisionVersion, c.CreatedBy, c.CreatedAtUtc);
}

public sealed class TerminateContractCommandHandler(
    IContractRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser) : ICommandHandler<TerminateContractCommand, Result<ContractResponse>>
{
    public async Task<Result<ContractResponse>> HandleAsync(TerminateContractCommand request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        string userId = currentUser.UserId?.ToString() ?? "system";
        var contract = await repository.GetByIdAsync(request.ContractId, tenantId, ct);
        if (contract is null)
            return Result.Failure<ContractResponse>(ContractErrors.NotFound);

        var result = contract.Terminate(request.Reason, userId);
        if (result.IsFailure)
            return Result.Failure<ContractResponse>(result.Error);

        await repository.UpdateAsync(contract, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ToResponse(contract));
    }

    private static ContractResponse ToResponse(Contract c) => new(
        c.Id, c.TenantId, c.ContractNumber, c.VendorId, c.Type, c.Currency,
        c.StartDate, c.EndDate, c.CapValue, c.ConsumedValue, c.ConsumedPercent,
        c.Notes, c.Status, c.RevisionVersion, c.CreatedBy, c.CreatedAtUtc);
}

public sealed class CancelContractCommandHandler(
    IContractRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser) : ICommandHandler<CancelContractCommand, Result<ContractResponse>>
{
    public async Task<Result<ContractResponse>> HandleAsync(CancelContractCommand request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        string userId = currentUser.UserId?.ToString() ?? "system";
        var contract = await repository.GetByIdAsync(request.ContractId, tenantId, ct);
        if (contract is null)
            return Result.Failure<ContractResponse>(ContractErrors.NotFound);

        var result = contract.Cancel(request.Reason, userId);
        if (result.IsFailure)
            return Result.Failure<ContractResponse>(result.Error);

        await repository.UpdateAsync(contract, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ToResponse(contract));
    }

    private static ContractResponse ToResponse(Contract c) => new(
        c.Id, c.TenantId, c.ContractNumber, c.VendorId, c.Type, c.Currency,
        c.StartDate, c.EndDate, c.CapValue, c.ConsumedValue, c.ConsumedPercent,
        c.Notes, c.Status, c.RevisionVersion, c.CreatedBy, c.CreatedAtUtc);
}

public sealed class RecordContractConsumptionCommandHandler(
    IContractRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant) : ICommandHandler<RecordContractConsumptionCommand, Result<ContractResponse>>
{
    public async Task<Result<ContractResponse>> HandleAsync(RecordContractConsumptionCommand request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        var contract = await repository.GetByIdAsync(request.ContractId, tenantId, ct);
        if (contract is null)
            return Result.Failure<ContractResponse>(ContractErrors.NotFound);

        var result = contract.RecordConsumption(request.Amount);
        if (result.IsFailure)
            return Result.Failure<ContractResponse>(result.Error);

        await repository.UpdateAsync(contract, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(ToResponse(contract));
    }

    private static ContractResponse ToResponse(Contract c) => new(
        c.Id, c.TenantId, c.ContractNumber, c.VendorId, c.Type, c.Currency,
        c.StartDate, c.EndDate, c.CapValue, c.ConsumedValue, c.ConsumedPercent,
        c.Notes, c.Status, c.RevisionVersion, c.CreatedBy, c.CreatedAtUtc);
}

public sealed class AddContractLineCommandHandler(
    IContractRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant) : ICommandHandler<AddContractLineCommand, Result<ContractLineResponse>>
{
    public async Task<Result<ContractLineResponse>> HandleAsync(AddContractLineCommand request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        var contract = await repository.GetByIdAsync(request.ContractId, tenantId, ct);
        if (contract is null)
            return Result.Failure<ContractLineResponse>(ContractErrors.NotFound);

        var line = new ContractLine(
            Guid.NewGuid(), request.ItemId, request.FreeText,
            request.UnitPrice, request.MinQuantity, request.EscalationJson, request.Notes);

        contract.AddLine(line);
        await repository.UpdateAsync(contract, ct);
        await unitOfWork.CommitAsync(ct);

        return Result.Success(new ContractLineResponse(
            line.Id, line.ItemId, line.FreeText, line.UnitPrice,
            line.MinQuantity, line.EscalationJson, line.Notes));
    }
}

public sealed class AddContractDocumentCommandHandler(
    IContractRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser) : ICommandHandler<AddContractDocumentCommand, Result<ContractDocumentResponse>>
{
    public async Task<Result<ContractDocumentResponse>> HandleAsync(AddContractDocumentCommand request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        string userId = currentUser.UserId?.ToString() ?? "system";
        var contract = await repository.GetByIdAsync(request.ContractId, tenantId, ct);
        if (contract is null)
            return Result.Failure<ContractDocumentResponse>(ContractErrors.NotFound);

        var doc = new ContractDocument(
            Guid.NewGuid(), request.DocumentType, request.S3Key,
            request.ExpiryDate, userId);
        contract.AddDocument(doc);
        await repository.UpdateAsync(contract, ct);
        await unitOfWork.CommitAsync(ct);

        return Result.Success(new ContractDocumentResponse(
            doc.Id, doc.DocumentType, doc.S3Key, doc.ExpiryDate,
            doc.UploadedBy, doc.UploadedAtUtc));
    }
}

public sealed class AddContractMilestoneCommandHandler(
    IContractRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant) : ICommandHandler<AddContractMilestoneCommand, Result<ContractMilestoneResponse>>
{
    public async Task<Result<ContractMilestoneResponse>> HandleAsync(AddContractMilestoneCommand request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        var contract = await repository.GetByIdAsync(request.ContractId, tenantId, ct);
        if (contract is null)
            return Result.Failure<ContractMilestoneResponse>(ContractErrors.NotFound);

        var milestone = new ContractMilestone(
            Guid.NewGuid(), request.Title, request.DueDate,
            request.Deliverables, request.SlaJson);
        contract.AddMilestone(milestone);
        await repository.UpdateAsync(contract, ct);
        await unitOfWork.CommitAsync(ct);

        return Result.Success(new ContractMilestoneResponse(
            milestone.Id, milestone.Title, milestone.DueDate,
            milestone.Deliverables, milestone.SlaJson,
            milestone.IsCompleted, milestone.CompletedAtUtc));
    }
}

public sealed class CompleteContractMilestoneCommandHandler(
    IContractRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant) : ICommandHandler<CompleteContractMilestoneCommand, Result<ContractMilestoneResponse>>
{
    public async Task<Result<ContractMilestoneResponse>> HandleAsync(CompleteContractMilestoneCommand request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        var contract = await repository.GetByIdAsync(request.ContractId, tenantId, ct);
        if (contract is null)
            return Result.Failure<ContractMilestoneResponse>(ContractErrors.NotFound);

        var milestone = contract.Milestones.FirstOrDefault(m => m.Id == request.MilestoneId);
        if (milestone is null)
            return Result.Failure<ContractMilestoneResponse>(ContractErrors.MilestoneNotFound);

        milestone.MarkCompleted();
        await repository.UpdateAsync(contract, ct);
        await unitOfWork.CommitAsync(ct);

        return Result.Success(new ContractMilestoneResponse(
            milestone.Id, milestone.Title, milestone.DueDate,
            milestone.Deliverables, milestone.SlaJson,
            milestone.IsCompleted, milestone.CompletedAtUtc));
    }
}

internal static class ContractErrors
{
    public static readonly Error NotFound = Error.NotFound("Contract.NotFound", "Contract not found");
    public static readonly Error MilestoneNotFound = Error.NotFound("Contract.MilestoneNotFound", "Contract milestone not found");
}
