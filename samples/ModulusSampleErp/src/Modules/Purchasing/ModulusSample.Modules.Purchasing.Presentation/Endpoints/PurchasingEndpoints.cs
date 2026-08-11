using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Purchasing.Application.Commands;
using ModulusSample.Modules.Purchasing.Application.Queries;

namespace ModulusSample.Modules.Purchasing.Presentation.Endpoints;

public static class PurchasingEndpoints
{
    public static void MapPurchasingEndpoints(this IEndpointRouteBuilder app)
    {
        MapRequisitionEndpoints(app);
        MapOrderEndpoints(app);
        MapReceiptEndpoints(app);
    }

    private static void MapRequisitionEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/purchase-requisitions")
            .WithName("PurchaseRequisitions")
            ;

        group.MapPost("/", CreateRequisition)
            .WithName("CreateRequisition")
            ;

        group.MapPost("/{id}/submit", SubmitRequisition)
            .WithName("SubmitRequisition")
            ;

        group.MapPost("/{id}/approve", ApproveRequisition)
            .WithName("ApproveRequisition")
            ;

        group.MapGet("/{id}", GetRequisitionById)
            .WithName("GetRequisitionById")
            ;

        group.MapGet("/", ListRequisitions)
            .WithName("ListRequisitions")
            ;
    }

    private static void MapOrderEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/purchase-orders")
            .WithName("PurchaseOrders")
            ;

        group.MapPost("/", CreateOrder)
            .WithName("CreateOrder")
            ;

        group.MapGet("/{id}", GetOrderById)
            .WithName("GetOrderById")
            ;

        group.MapGet("/", ListOrders)
            .WithName("ListOrders")
            ;
    }

    private static void MapReceiptEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/goods-receipts")
            .WithName("GoodsReceipts")
            ;

        group.MapPost("/", CreateReceipt)
            .WithName("CreateReceipt")
            ;

        group.MapGet("/{id}", GetReceiptById)
            .WithName("GetReceiptById")
            ;

        group.MapGet("/", ListReceipts)
            .WithName("ListReceipts")
            ;
    }

    // Requisition Handlers
    private static async Task<IResult> CreateRequisition(
        IMediator mediator,
        CreateRequisitionRequest request)
    {
        var command = new CreatePurchaseRequisitionCommand(request.RequisitionNumber, request.OrgUnitId);
        var result = await mediator.SendAsync(command);

        return result.IsSuccess
            ? Results.Created($"/api/purchase-requisitions/{result.Value}", new { id = result.Value })
            : Results.BadRequest(new { error = result.Error?.Message });
    }

    private static async Task<IResult> SubmitRequisition(
        IMediator mediator,
        Guid id)
    {
        var command = new SubmitPurchaseRequisitionCommand(id);
        var result = await mediator.SendAsync(command);

        return result.IsSuccess
            ? Results.Ok(new { message = "Requisition submitted" })
            : Results.BadRequest(new { error = result.Error?.Message });
    }

    private static async Task<IResult> ApproveRequisition(
        IMediator mediator,
        Guid id,
        ApproveRequisitionRequest request)
    {
        var command = new ApprovePurchaseRequisitionCommand(id, request.ApproverId);
        var result = await mediator.SendAsync(command);

        return result.IsSuccess
            ? Results.Ok(new { message = "Requisition approved" })
            : Results.BadRequest(new { error = result.Error?.Message });
    }

    private static async Task<IResult> GetRequisitionById(
        IMediator mediator,
        Guid id)
    {
        var result = await mediator.QueryAsync(new GetRequisitionByIdQuery(id));
        return result is not null ? Results.Ok(result) : Results.NotFound();
    }

    private static async Task<IResult> ListRequisitions(
        IMediator mediator,
        int page = 1,
        int pageSize = 10)
    {
        var result = await mediator.QueryAsync(new ListRequisitionsQuery(page, pageSize));
        return Results.Ok(result);
    }

    // Order Handlers
    private static async Task<IResult> CreateOrder(
        IMediator mediator,
        CreateOrderRequest request)
    {
        var command = new CreatePurchaseOrderCommand(
            request.OrderNumber,
            request.RequisitionId,
            request.SupplierId,
            request.OrgUnitId);
        var result = await mediator.SendAsync(command);

        return result.IsSuccess
            ? Results.Created($"/api/purchase-orders/{result.Value}", new { id = result.Value })
            : Results.BadRequest(new { error = result.Error?.Message });
    }

    private static async Task<IResult> GetOrderById(
        IMediator mediator,
        Guid id)
    {
        var result = await mediator.QueryAsync(new GetOrderByIdQuery(id));
        return result is not null ? Results.Ok(result) : Results.NotFound();
    }

    private static async Task<IResult> ListOrders(
        IMediator mediator,
        int page = 1,
        int pageSize = 10)
    {
        var result = await mediator.QueryAsync(new ListOrdersQuery(page, pageSize));
        return Results.Ok(result);
    }

    // Receipt Handlers
    private static async Task<IResult> CreateReceipt(
        IMediator mediator,
        CreateReceiptRequest request)
    {
        var command = new CreateGoodsReceiptCommand(
            request.ReceiptNumber,
            request.PurchaseOrderId,
            request.OrgUnitId);
        var result = await mediator.SendAsync(command);

        return result.IsSuccess
            ? Results.Created($"/api/goods-receipts/{result.Value}", new { id = result.Value })
            : Results.BadRequest(new { error = result.Error?.Message });
    }

    private static async Task<IResult> GetReceiptById(
        IMediator mediator,
        Guid id)
    {
        var result = await mediator.QueryAsync(new GetReceiptByIdQuery(id));
        return result is not null ? Results.Ok(result) : Results.NotFound();
    }

    private static async Task<IResult> ListReceipts(
        IMediator mediator,
        int page = 1,
        int pageSize = 10)
    {
        var result = await mediator.QueryAsync(new ListReceiptsQuery(page, pageSize));
        return Results.Ok(result);
    }
}

public sealed record CreateRequisitionRequest(string RequisitionNumber, Guid OrgUnitId);
public sealed record ApproveRequisitionRequest(Guid ApproverId);
public sealed record CreateOrderRequest(string OrderNumber, Guid RequisitionId, Guid SupplierId, Guid OrgUnitId);
public sealed record CreateReceiptRequest(string ReceiptNumber, Guid PurchaseOrderId, Guid OrgUnitId);
