using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using ProcureFlow.Modules.Costing.Application.Commands;
using ProcureFlow.Modules.Costing.Application.Dtos;
using ProcureFlow.Modules.Costing.Domain.Entities;
using ProcureFlow.Modules.Costing.Domain.Repositories;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Costing.Application.Commands;

public sealed class CreateLandedCostSheetHandler(
    ILandedCostSheetRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant) : ICommandHandler<CreateLandedCostSheetCommand, Result<LandedCostSheetResponse>>
{
    public async Task<Result<LandedCostSheetResponse>> HandleAsync(CreateLandedCostSheetCommand request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        LandedCostSheet sheet;
        try
        {
            sheet = LandedCostSheet.Create(tenantId, request.FileId, request.SheetNumber, request.Currency);
            foreach (CostSheetLineInput line in request.Lines)
            {
                sheet.AddLine(line.SourceLineId, line.GoodsValueFcy, line.GoodsValueBdt, line.ReceivedQty,
                    line.NetWeightKg, line.GrossWeightKg, line.VolumeCbm, line.ContainerShare);
            }
        }
        catch (ArgumentException ex)
        {
            return Result.Failure<LandedCostSheetResponse>(Error.Validation("Lcs.Line", ex.Message));
        }

        await repository.AddAsync(sheet, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(CostingResponseFactory.ToSheetResponse(sheet));
    }
}

public sealed class AddCostElementHandler(
    ILandedCostSheetRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<AddCostElementCommand, Result<LandedCostSheetResponse>>
{
    public async Task<Result<LandedCostSheetResponse>> HandleAsync(AddCostElementCommand request, CancellationToken ct)
    {
        LandedCostSheet? sheet = await repository.GetByIdAsync(request.SheetId, ct);
        if (sheet is null)
            return Result.Failure<LandedCostSheetResponse>(Error.NotFound("Lcs.NotFound", "Cost sheet not found"));

        var element = new CostElement(Guid.NewGuid(), request.Name, request.AmountFcy, request.FxRate,
            request.AmountBdt, request.Driver, request.Scope, request.Treatment, request.SourceDocType,
            request.SourceDocNumber, request.SelectedLineIds);

        Result result = sheet.AddElement(element);
        if (result.IsFailure)
            return Result.Failure<LandedCostSheetResponse>(result.Error);

        await repository.SaveAsync(sheet, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(CostingResponseFactory.ToSheetResponse(sheet));
    }
}

public sealed class AllocateCostsHandler(
    ILandedCostSheetRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<AllocateCostsCommand, Result<LandedCostSheetResponse>>
{
    public async Task<Result<LandedCostSheetResponse>> HandleAsync(AllocateCostsCommand request, CancellationToken ct)
    {
        LandedCostSheet? sheet = await repository.GetByIdAsync(request.SheetId, ct);
        if (sheet is null)
            return Result.Failure<LandedCostSheetResponse>(Error.NotFound("Lcs.NotFound", "Cost sheet not found"));

        Result result = sheet.Allocate();
        if (result.IsFailure)
            return Result.Failure<LandedCostSheetResponse>(result.Error);

        await repository.SaveAsync(sheet, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(CostingResponseFactory.ToSheetResponse(sheet));
    }
}

public sealed class FinalizeCostSheetHandler(
    ILandedCostSheetRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<FinalizeCostSheetCommand, Result<LandedCostSheetResponse>>
{
    public async Task<Result<LandedCostSheetResponse>> HandleAsync(FinalizeCostSheetCommand request, CancellationToken ct)
    {
        LandedCostSheet? sheet = await repository.GetByIdAsync(request.SheetId, ct);
        if (sheet is null)
            return Result.Failure<LandedCostSheetResponse>(Error.NotFound("Lcs.NotFound", "Cost sheet not found"));

        Result result = sheet.SubmitForFinalization();
        if (result.IsFailure)
            return Result.Failure<LandedCostSheetResponse>(result.Error);

        await repository.SaveAsync(sheet, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(CostingResponseFactory.ToSheetResponse(sheet));
    }
}

public sealed class OpenAdjustmentHandler(
    ILandedCostSheetRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<OpenAdjustmentCommand, Result<LandedCostSheetResponse>>
{
    public async Task<Result<LandedCostSheetResponse>> HandleAsync(OpenAdjustmentCommand request, CancellationToken ct)
    {
        LandedCostSheet? sheet = await repository.GetByIdAsync(request.SheetId, ct);
        if (sheet is null)
            return Result.Failure<LandedCostSheetResponse>(Error.NotFound("Lcs.NotFound", "Cost sheet not found"));

        Result result = sheet.OpenAdjustment();
        if (result.IsFailure)
            return Result.Failure<LandedCostSheetResponse>(result.Error);

        await repository.SaveAsync(sheet, ct);
        await unitOfWork.CommitAsync(ct);
        return Result.Success(CostingResponseFactory.ToSheetResponse(sheet));
    }
}