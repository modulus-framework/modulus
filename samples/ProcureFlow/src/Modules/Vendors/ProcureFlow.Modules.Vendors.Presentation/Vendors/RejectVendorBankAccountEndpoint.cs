using ProcureFlow.Modules.Vendors.Application.Vendors.Dtos;
using Modulus.Mediator.Abstractions;
using ProcureFlow.Modules.Vendors.Application.Vendors.Commands;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Vendors.Presentation.Vendors;

internal sealed class RejectVendorBankAccountEndpoint : Endpoint<RejectVendorBankAccountEndpoint.Request, object>
{
    private readonly IMediator _mediator;

    public RejectVendorBankAccountEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/vendors/{vendorId}/bank-accounts/{bankAccountId}/reject");
        Tag(Tags.Vendors);
        Summary("Reject a bank account (BR-VEN-06)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result result = await _mediator.SendAsync(new RejectVendorBankAccountCommand(req.VendorId, req.BankAccountId, req.Reason), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid VendorId { get; set; }
        public Guid BankAccountId { get; set; }
        public string Reason { get; set; } = string.Empty;
    }
}
