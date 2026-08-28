using ProcureFlow.Modules.Vendors.Application.Vendors.Dtos;
using Modulus.Mediator.Abstractions;
using ProcureFlow.Modules.Vendors.Application.Vendors.Commands;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Vendors.Presentation.Vendors;

internal sealed class AddVendorBankAccountEndpoint : Endpoint<AddVendorBankAccountEndpoint.Request, object>
{
    private readonly IMediator _mediator;

    public AddVendorBankAccountEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/vendors/{vendorId}/bank-accounts");
        Tag(Tags.Vendors);
        Summary("Add a bank account (maker step, BR-VEN-06)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result result = await _mediator.SendAsync(new AddVendorBankAccountCommand(
            req.VendorId, req.BankName, req.AccountName, req.AccountNumber, req.Branch, req.SwiftCode), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid VendorId { get; set; }
        public string BankName { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public string AccountNumber { get; set; } = string.Empty;
        public string Branch { get; set; } = string.Empty;
        public string SwiftCode { get; set; } = string.Empty;
    }
}
