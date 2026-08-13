using Modulus.Mediator.Abstractions;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Purchasing.Application.Orders.Commands;

public sealed record SendPurchaseOrderCommand(
    Guid OrderId) : ICommand<Result>;