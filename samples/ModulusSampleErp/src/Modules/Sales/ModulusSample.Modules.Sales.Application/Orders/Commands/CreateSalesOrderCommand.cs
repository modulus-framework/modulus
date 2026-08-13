using Modulus.Mediator.Abstractions;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Sales.Application.Orders.Commands;

public sealed record CreateSalesOrderCommand(
    string OrderNumber,
    Guid CustomerId,
    Guid OrgUnitId) : ICommand<Result<Guid>>;