using Microsoft.EntityFrameworkCore;
using TradeFlow.Modules.Vendors.Infrastructure.Database;
using TradeFlow.Modules.Vendors.PublicApi;

namespace TradeFlow.Modules.Vendors.Infrastructure.PublicApi;

/// <summary>
/// BRS §5.3 sync surface into the Vendors module, consumed by Procurement.
/// Implements <see cref="IVendorPublicApi"/>.
/// </summary>
public sealed class VendorPublicApi(VendorsDbContext context) : IVendorPublicApi
{
    public async Task<bool> IsVendorEligibleAsync(Guid vendorId, CancellationToken ct = default)
    {
        var vendor = await context.Vendors
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == vendorId, ct);

        return vendor?.CanTransact() ?? false;
    }

    public async Task<bool> IsQualifiedForCategoryAsync(Guid vendorId, string category, CancellationToken ct = default)
    {
        var vendor = await context.Vendors
            .AsNoTracking()
            .Include(v => v.Qualifications)
            .FirstOrDefaultAsync(v => v.Id == vendorId, ct);

        return vendor?.IsQualifiedForCategory(category, DateOnly.FromDateTime(DateTime.UtcNow)) ?? false;
    }
}
