using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Billing.Application.Commands;
using ModulusSample.Modules.Billing.Application.Queries;

namespace ModulusSample.Modules.Billing.Presentation.Endpoints;

public static class BillingEndpoints
{
    public static void MapBillingEndpoints(this WebApplication app)
    {
        MapInvoiceEndpoints(app);
        MapPaymentEndpoints(app);
        MapCreditNoteEndpoints(app);
    }

    private static void MapInvoiceEndpoints(WebApplication app)
    {
        var group = app.MapGroup("/api/invoices")
            .WithName("Invoices")
            .WithDescription("Manage customer invoices")
            .WithOpenApi();

        group.MapPost("/", CreateInvoice)
            .WithName("CreateInvoice")
            .WithDescription("Create a new invoice");

        group.MapPost("/{id}/lines", AddInvoiceLine)
            .WithName("AddInvoiceLine")
            .WithDescription("Add a line to an invoice");

        group.MapPost("/{id}/send", SendInvoice)
            .WithName("SendInvoice")
            .WithDescription("Send an invoice to customer");

        group.MapPost("/{id}/pay", MarkInvoiceAsPaid)
            .WithName("MarkInvoiceAsPaid")
            .WithDescription("Mark an invoice as paid");

        group.MapPost("/{id}/overdue", MarkInvoiceAsOverdue)
            .WithName("MarkInvoiceAsOverdue")
            .WithDescription("Mark an invoice as overdue");

        group.MapGet("/{id}", GetInvoiceById)
            .WithName("GetInvoiceById")
            .WithDescription("Get invoice details");

        group.MapGet("/", ListInvoices)
            .WithName("ListInvoices")
            .WithDescription("List all invoices");
    }

    private static void MapPaymentEndpoints(WebApplication app)
    {
        var group = app.MapGroup("/api/payments")
            .WithName("Payments")
            .WithDescription("Manage invoice payments")
            .WithOpenApi();

        group.MapPost("/", CreatePayment)
            .WithName("CreatePayment")
            .WithDescription("Create a new payment");

        group.MapPost("/{id}/confirm", ConfirmPayment)
            .WithName("ConfirmPayment")
            .WithDescription("Confirm a payment");

        group.MapGet("/{id}", GetPaymentById)
            .WithName("GetPaymentById")
            .WithDescription("Get payment details");

        group.MapGet("/", ListPayments)
            .WithName("ListPayments")
            .WithDescription("List all payments");
    }

    private static void MapCreditNoteEndpoints(WebApplication app)
    {
        var group = app.MapGroup("/api/credit-notes")
            .WithName("CreditNotes")
            .WithDescription("Manage credit notes")
            .WithOpenApi();

        group.MapPost("/", CreateCreditNote)
            .WithName("CreateCreditNote")
            .WithDescription("Create a new credit note");

        group.MapPost("/{id}/issue", IssueCreditNote)
            .WithName("IssueCreditNote")
            .WithDescription("Issue a credit note");

        group.MapPost("/{id}/apply", ApplyCreditNote)
            .WithName("ApplyCreditNote")
            .WithDescription("Apply a credit note to invoice");

        group.MapGet("/{id}", GetCreditNoteById)
            .WithName("GetCreditNoteById")
            .WithDescription("Get credit note details");

        group.MapGet("/", ListCreditNotes)
            .WithName("ListCreditNotes")
            .WithDescription("List all credit notes");
    }

    private static async Task<IResult> CreateInvoice(CreateInvoiceCommand command, IMediator mediator)
    {
        var result = await mediator.SendAsync(command);
        return result.IsSuccess
            ? Results.Created($"/api/invoices/{result.Value}", new { id = result.Value })
            : Results.BadRequest(result.Error);
    }

    private static async Task<IResult> AddInvoiceLine(Guid id, AddInvoiceLineCommand command, IMediator mediator)
    {
        var lineCommand = command with { InvoiceId = id };
        var result = await mediator.SendAsync(lineCommand);
        return result.IsSuccess ? Results.Ok() : Results.BadRequest(result.Error);
    }

    private static async Task<IResult> SendInvoice(Guid id, IMediator mediator)
    {
        var result = await mediator.SendAsync(new SendInvoiceCommand(id));
        return result.IsSuccess ? Results.Ok() : Results.BadRequest(result.Error);
    }

    private static async Task<IResult> MarkInvoiceAsPaid(Guid id, IMediator mediator)
    {
        var result = await mediator.SendAsync(new MarkInvoiceAsPaidCommand(id));
        return result.IsSuccess ? Results.Ok() : Results.BadRequest(result.Error);
    }

    private static async Task<IResult> MarkInvoiceAsOverdue(Guid id, IMediator mediator)
    {
        var result = await mediator.SendAsync(new MarkInvoiceAsOverdueCommand(id));
        return result.IsSuccess ? Results.Ok() : Results.BadRequest(result.Error);
    }

    private static async Task<IResult> GetInvoiceById(Guid id, IMediator mediator)
    {
        var result = await mediator.QueryAsync(new GetInvoiceByIdQuery(id));
        return result is not null ? Results.Ok(result) : Results.NotFound();
    }

    private static async Task<IResult> ListInvoices(int pageNumber, int pageSize, IMediator mediator)
    {
        var result = await mediator.QueryAsync(new ListInvoicesQuery(pageNumber, pageSize));
        return Results.Ok(result);
    }

    private static async Task<IResult> CreatePayment(CreatePaymentCommand command, IMediator mediator)
    {
        var result = await mediator.SendAsync(command);
        return result.IsSuccess
            ? Results.Created($"/api/payments/{result.Value}", new { id = result.Value })
            : Results.BadRequest(result.Error);
    }

    private static async Task<IResult> ConfirmPayment(Guid id, ConfirmPaymentCommand command, IMediator mediator)
    {
        var confirmCommand = command with { PaymentId = id };
        var result = await mediator.SendAsync(confirmCommand);
        return result.IsSuccess ? Results.Ok() : Results.BadRequest(result.Error);
    }

    private static async Task<IResult> GetPaymentById(Guid id, IMediator mediator)
    {
        var result = await mediator.QueryAsync(new GetPaymentByIdQuery(id));
        return result is not null ? Results.Ok(result) : Results.NotFound();
    }

    private static async Task<IResult> ListPayments(int pageNumber, int pageSize, IMediator mediator)
    {
        var result = await mediator.QueryAsync(new ListPaymentsQuery(pageNumber, pageSize));
        return Results.Ok(result);
    }

    private static async Task<IResult> CreateCreditNote(CreateCreditNoteCommand command, IMediator mediator)
    {
        var result = await mediator.SendAsync(command);
        return result.IsSuccess
            ? Results.Created($"/api/credit-notes/{result.Value}", new { id = result.Value })
            : Results.BadRequest(result.Error);
    }

    private static async Task<IResult> IssueCreditNote(Guid id, IMediator mediator)
    {
        var result = await mediator.SendAsync(new IssueCreditNoteCommand(id));
        return result.IsSuccess ? Results.Ok() : Results.BadRequest(result.Error);
    }

    private static async Task<IResult> ApplyCreditNote(Guid id, IMediator mediator)
    {
        var result = await mediator.SendAsync(new ApplyCreditNoteCommand(id));
        return result.IsSuccess ? Results.Ok() : Results.BadRequest(result.Error);
    }

    private static async Task<IResult> GetCreditNoteById(Guid id, IMediator mediator)
    {
        var result = await mediator.QueryAsync(new GetCreditNoteByIdQuery(id));
        return result is not null ? Results.Ok(result) : Results.NotFound();
    }

    private static async Task<IResult> ListCreditNotes(int pageNumber, int pageSize, IMediator mediator)
    {
        var result = await mediator.QueryAsync(new ListCreditNotesQuery(pageNumber, pageSize));
        return Results.Ok(result);
    }
}
