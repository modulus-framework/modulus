using Modulus.Mediator.Abstractions;
using TradeFlow.Modules.Vendors.Application.Vendors.Commands;
using TradeFlow.Modules.Vendors.Domain.Entities;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Vendors.Presentation.Vendors;

internal sealed class UpdateVendorEndpoint : Endpoint<UpdateVendorEndpoint.UpdateVendorRequest>
{
    private readonly IMediator _mediator;

    public UpdateVendorEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Put("/vendors/{vendorId}");
        Tag(Tags.Vendors);
        Summary("Update vendor profile fields");
    }

    public override async Task HandleAsync(UpdateVendorRequest req, CancellationToken ct)
    {
        var command = new UpdateVendorCommand(
            req.VendorId,
            req.Name,
            req.LegalName,
            req.Country,
            req.VendorType,
            req.Tin,
            req.Bin,
            req.Email,
            req.Phone,
            req.Address);

        Result result = await _mediator.SendAsync(command, ct);
        await EndpointHelper.ResolveAsync(HttpContext, result, ct);
    }

    internal sealed class UpdateVendorRequest
    {
        public Guid VendorId { get; set; }
        public string? Name { get; set; }
        public string? LegalName { get; set; }
        public string? Country { get; set; }
        public VendorType? VendorType { get; set; }
        public string? Tin { get; set; }
        public string? Bin { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Address { get; set; }
    }
}
