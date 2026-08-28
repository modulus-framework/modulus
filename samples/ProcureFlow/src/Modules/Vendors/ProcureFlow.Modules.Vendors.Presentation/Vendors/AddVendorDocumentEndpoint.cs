using Modulus.Mediator.Abstractions;
using ProcureFlow.Modules.Vendors.Application.Vendors.Commands;
using ProcureFlow.Modules.Vendors.Application.Vendors.Dtos;
using ProcureFlow.Modules.Vendors.Domain.Entities;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Vendors.Presentation.Vendors;

internal sealed class AddVendorDocumentEndpoint : Endpoint<AddVendorDocumentEndpoint.Request, VendorDocumentResponse>
{
    private readonly IMediator _mediator;

    public AddVendorDocumentEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/vendors/{vendorId}/documents");
        Tag(Tags.Vendors);
        Summary("Add a KYC document (BR-VEN-03)");
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        Result<VendorDocumentResponse> result = await _mediator.SendAsync(new AddVendorDocumentCommand(
            req.VendorId, req.DocumentType, req.DocumentNumber, req.S3Key, req.ExpiryDate), ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class Request
    {
        public Guid VendorId { get; set; }
        public VendorDocumentType DocumentType { get; set; }
        public string DocumentNumber { get; set; } = string.Empty;
        public string S3Key { get; set; } = string.Empty;
        public DateOnly? ExpiryDate { get; set; }
    }
}
