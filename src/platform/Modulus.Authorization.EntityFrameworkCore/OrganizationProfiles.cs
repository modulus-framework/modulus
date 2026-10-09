using Microsoft.EntityFrameworkCore;

namespace Modulus.Authorization.EntityFrameworkCore;

/// <summary>What an org unit is. Open vocabulary; these are the common ones (compared case-insensitively).</summary>
public static class OrgUnitKinds
{
    /// <summary>A branch.</summary>
    public const string Branch = "branch";

    /// <summary>A factory or plant.</summary>
    public const string Factory = "factory";

    /// <summary>An office.</summary>
    public const string Office = "office";

    /// <summary>A warehouse or store.</summary>
    public const string Warehouse = "warehouse";

    /// <summary>A department.</summary>
    public const string Department = "department";

    /// <summary>A team.</summary>
    public const string Team = "team";
}

/// <summary>The business details of an org unit, kept beside its place in the hierarchy.</summary>
/// <param name="UnitId">The hierarchy unit this describes.</param>
/// <param name="Code">A short code, unique within the company.</param>
/// <param name="Name">The display name.</param>
/// <param name="Kind">What it is (<see cref="OrgUnitKinds"/>, or any other word).</param>
/// <param name="IsClosed">Whether the unit is closed (no longer operating).</param>
/// <param name="ManagerUserId">The user who manages it, or null.</param>
/// <param name="ClosedAt">When it was closed.</param>
public sealed record OrgUnitProfile(
    Guid UnitId, string Code, string Name, string Kind, bool IsClosed = false, Guid? ManagerUserId = null, DateTimeOffset? ClosedAt = null);

/// <summary>The legal and regional details of a company (a Modulus tenant).</summary>
/// <param name="LegalName">The registered legal name.</param>
/// <param name="TradeName">The trading name, if different.</param>
/// <param name="RegistrationNumber">The company registration number.</param>
/// <param name="TaxId">The tax or VAT identifier.</param>
/// <param name="Address">The legal address.</param>
/// <param name="Country">The country (ISO 3166-1 alpha-2).</param>
/// <param name="Currency">The functional currency (ISO 4217).</param>
/// <param name="FiscalYearStartMonth">The month the financial year starts, 1 to 12.</param>
/// <param name="TimeZone">The default IANA time zone.</param>
/// <param name="Language">The default language tag.</param>
public sealed record CompanyProfile(
    string LegalName, string? TradeName = null, string? RegistrationNumber = null, string? TaxId = null, string? Address = null,
    string? Country = null, string? Currency = null, int FiscalYearStartMonth = 1, string? TimeZone = null, string? Language = null);

/// <summary>The company's profile and its org units' profiles, in the authorization store (tenant-filtered).</summary>
public sealed class EfOrganizationProfileStore(IDbContextFactory<AuthorizationStoreDbContext> factory)
{
    /// <summary>The company profile of the current company; null until one is saved.</summary>
    public async Task<CompanyProfile?> GetCompanyAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.CompanyProfiles.AsNoTracking().SingleOrDefaultAsync(ct);
        return row is null
            ? null
            : new CompanyProfile(row.LegalName, row.TradeName, row.RegistrationNumber, row.TaxId, row.Address, row.Country,
                row.Currency, row.FiscalYearStartMonth, row.TimeZone, row.Language);
    }

    /// <summary>Saves the company profile (replacing any earlier one).</summary>
    public async Task SaveCompanyAsync(CompanyProfile profile, DateTimeOffset now, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(profile.LegalName);
        if (profile.FiscalYearStartMonth is < 1 or > 12)
            throw new ArgumentOutOfRangeException(nameof(profile), "The financial year starts in month 1 to 12.");

        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.CompanyProfiles.SingleOrDefaultAsync(ct);
        if (row is null)
        {
            row = new CompanyProfileRow();
            db.CompanyProfiles.Add(row);
        }

        row.LegalName = profile.LegalName.Trim();
        row.TradeName = Clean(profile.TradeName);
        row.RegistrationNumber = Clean(profile.RegistrationNumber);
        row.TaxId = Clean(profile.TaxId);
        row.Address = Clean(profile.Address);
        row.Country = Clean(profile.Country)?.ToUpperInvariant();
        row.Currency = Clean(profile.Currency)?.ToUpperInvariant();
        row.FiscalYearStartMonth = profile.FiscalYearStartMonth;
        row.TimeZone = Clean(profile.TimeZone);
        row.Language = Clean(profile.Language);
        row.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Every unit profile of the current company, by code.</summary>
    public async Task<IReadOnlyList<OrgUnitProfile>> ListUnitsAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.OrgUnitProfiles.AsNoTracking().ToListAsync(ct);
        return [.. rows.OrderBy(r => r.Code, StringComparer.OrdinalIgnoreCase).Select(ToProfile)];
    }

    /// <summary>One unit's profile; null when none was saved.</summary>
    public async Task<OrgUnitProfile?> GetUnitAsync(Guid unitId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.OrgUnitProfiles.AsNoTracking().SingleOrDefaultAsync(p => p.UnitId == unitId, ct);
        return row is null ? null : ToProfile(row);
    }

    /// <summary>
    /// Saves a unit's business details. The code must be unique within the company (case-insensitive).
    /// </summary>
    /// <returns>False when another unit already uses the code.</returns>
    public async Task<bool> SaveUnitAsync(OrgUnitProfile profile, DateTimeOffset now, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(profile.Code);
        ArgumentException.ThrowIfNullOrWhiteSpace(profile.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(profile.Kind);

        var code = profile.Code.Trim();
        var normalized = code.ToUpperInvariant();
        await using var db = await factory.CreateDbContextAsync(ct);
        if (await db.OrgUnitProfiles.AnyAsync(p => p.NormalizedCode == normalized && p.UnitId != profile.UnitId, ct))
            return false;

        var row = await db.OrgUnitProfiles.SingleOrDefaultAsync(p => p.UnitId == profile.UnitId, ct);
        if (row is null)
        {
            row = new OrgUnitProfileRow { UnitId = profile.UnitId };
            db.OrgUnitProfiles.Add(row);
        }

        row.Code = code;
        row.NormalizedCode = normalized;
        row.Name = profile.Name.Trim();
        row.Kind = profile.Kind.Trim().ToLowerInvariant();
        row.ManagerUserId = profile.ManagerUserId;
        row.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Marks a unit closed or open again; false when it has no profile.</summary>
    public async Task<bool> SetClosedAsync(Guid unitId, bool closed, DateTimeOffset now, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.OrgUnitProfiles.SingleOrDefaultAsync(p => p.UnitId == unitId, ct);
        if (row is null)
            return false;

        row.IsClosed = closed;
        row.ClosedAt = closed ? now : null;
        row.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static OrgUnitProfile ToProfile(OrgUnitProfileRow r) => new(r.UnitId, r.Code, r.Name, r.Kind, r.IsClosed, r.ManagerUserId, r.ClosedAt);
}
