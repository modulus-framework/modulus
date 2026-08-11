namespace ModulusSample.Modules.Catalog.Application.Dtos;

public sealed record ProductDto(
    Guid Id,
    string Name,
    string? Description,
    decimal UnitCost,
    decimal? Margin,
    decimal ListPrice,
    Guid? CategoryId,
    bool IsActive);
