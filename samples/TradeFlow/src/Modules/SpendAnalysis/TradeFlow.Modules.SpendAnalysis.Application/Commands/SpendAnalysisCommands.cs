using Modulus.Mediator.Abstractions;

namespace TradeFlow.Modules.SpendAnalysis.Application.Commands;

// ── Category Taxonomy Commands ───────────────────────────────────────

public sealed record AddCategoryCommand(
    string Code,
    string Name,
    string? Description,
    Guid? ParentId
) : ICommand<Result<Guid>>;

public sealed record UpdateCategoryCommand(
    Guid CategoryId,
    string Name,
    string? Description,
    Guid? ParentId
) : ICommand<Result>;

public sealed record MapPoLineToCategoryCommand(
    Guid PoLineId,
    Guid CategoryId
) : ICommand<Result>;

public sealed record RefreshSpendCubeCommand(
    DateOnly Period
) : ICommand<Result>;
