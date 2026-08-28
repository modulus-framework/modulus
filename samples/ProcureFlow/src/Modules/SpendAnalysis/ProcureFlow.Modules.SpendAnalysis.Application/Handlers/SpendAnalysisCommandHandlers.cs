using Modulus.Mediator.Abstractions;
using ProcureFlow.Modules.SpendAnalysis.Application.Commands;
using ProcureFlow.Modules.SpendAnalysis.Domain.Entities;
using ProcureFlow.Modules.SpendAnalysis.Domain.Repositories;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.SpendAnalysis.Application.Handlers;

public sealed class AddCategoryCommandHandler : ICommandHandler<AddCategoryCommand, Result<Guid>>
{
    private readonly ISpendAnalysisUnitOfWork _unitOfWork;

    public AddCategoryCommandHandler(ISpendAnalysisUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<Result<Guid>> HandleAsync(AddCategoryCommand request, CancellationToken ct)
    {
        var existing = await _unitOfWork.Categories.GetByCodeAsync(request.Code, Guid.Empty, ct);
        if (existing is not null)
            return Result.Failure<Guid>(Error.Conflict("Category.CodeDuplicate", $"Category with code {request.Code} already exists"));

        var category = new CategoryTaxonomy(
            id: Guid.NewGuid(),
            tenantId: Guid.Empty,
            code: request.Code,
            name: request.Name,
            description: request.Description,
            parentId: request.ParentId,
            isActive: true,
            createdBy: "system");

        _unitOfWork.Categories.Add(category);
        await _unitOfWork.SaveChangesAsync(ct);
        return Result.Success(category.Id);
    }
}

public sealed class UpdateCategoryCommandHandler : ICommandHandler<UpdateCategoryCommand, Result>
{
    private readonly ISpendAnalysisUnitOfWork _unitOfWork;

    public UpdateCategoryCommandHandler(ISpendAnalysisUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<Result> HandleAsync(UpdateCategoryCommand request, CancellationToken ct)
    {
        var category = await _unitOfWork.Categories.GetByIdAsync(request.CategoryId, ct);
        if (category is null)
            return Result.Failure(Error.NotFound("Category.NotFound", "Category not found"));

        category.Update(request.Name, request.Description, request.ParentId, "system");
        _unitOfWork.Categories.Update(category);
        await _unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public sealed class MapPoLineToCategoryCommandHandler : ICommandHandler<MapPoLineToCategoryCommand, Result>
{
    private readonly ISpendAnalysisUnitOfWork _unitOfWork;

    public MapPoLineToCategoryCommandHandler(ISpendAnalysisUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<Result> HandleAsync(MapPoLineToCategoryCommand request, CancellationToken ct)
    {
        var category = await _unitOfWork.Categories.GetByIdAsync(request.CategoryId, ct);
        if (category is null)
            return Result.Failure(Error.NotFound("Category.NotFound", "Category not found"));

        var mapping = new PoLineCategoryMapping(
            id: Guid.NewGuid(),
            tenantId: Guid.Empty,
            poLineId: request.PoLineId,
            categoryId: request.CategoryId,
            isAutoClassified: false,
            confidenceScore: null,
            createdBy: "system");

        await _unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
