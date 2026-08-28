using ProcureFlow.Modules.Vendors.Application.Vendors.Dtos;
using Modulus.Mediator.Abstractions;
using ProcureFlow.Modules.Vendors.Application.Vendors.Commands;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Vendors.Presentation.Vendors;

internal sealed class ApproveVendorBankAccountEndpoint : Endpoint<ApproveVendorBankAccountEndpoint.Request, object>
{
    private readonly IMediator _mediator;

    public ApproveVendorBankAccountEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/vendors/{vendorId}/bank-accounts/{bankAccountId}/approve");
        Tag(Tags.Vendors);
        Summary("Approve a bank account (checker step, BR-VEN-06)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result result = await _mediator.SendAsync(new ApproveVendorBankAccountCommand(req.VendorId, req.BankAccountId), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid VendorId { get; set; }
        public Guid BankAccountId { get; set; }
    }
}
