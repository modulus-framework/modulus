using Modulus.Mediator.Abstractions;
using ProcureFlow.Modules.Vendors.Application.Vendors.Commands;
using ProcureFlow.Modules.Vendors.Application.Vendors.Dtos;
using ProcureFlow.Modules.Vendors.Domain.Entities;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Vendors.Presentation.Vendors;

internal sealed class CreateVendorEndpoint : Endpoint<CreateVendorEndpoint.CreateVendorRequest, CreateVendorResponse>
{
    private readonly IMediator _mediator;

    public CreateVendorEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/vendors");
        Tag(Tags.Vendors);
        Summary("Create a new vendor");
    }

    public override async Task HandleAsync(CreateVendorRequest req, CancellationToken ct)
    {
        var command = new CreateVendorCommand(
            req.Name,
            req.LegalName,
            req.Country,
            req.VendorType,
            req.Tin,
            req.Bin,
            req.Email,
            req.Phone,
            req.Address);

        Result<CreateVendorResponse> result = await _mediator.SendAsync(command, ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendCreatedAsync(result.Value, $"/api/v1/vendors/{result.Value.VendorId}", ct);
    }

    internal sealed class CreateVendorRequest
    {
        public string Name { get; set; } = string.Empty;
        public string LegalName { get; set; } = string.Empty;
        public string Country { get; set; } = string.Empty;
        public VendorType VendorType { get; set; }
        public string? Tin { get; set; }
        public string? Bin { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Address { get; set; }
    }
}
