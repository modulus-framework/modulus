namespace Modulus.Identity;

using System.Globalization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;
using Modulus.Identity.Abstractions;

/// <summary>Shared rules for shop-floor employee codes and PINs.</summary>
internal static class ShopFloorRules
{
    /// <summary>Upper-cases a code and checks it is made of letters, digits and <c>. _ -</c> (1 to 32 characters).</summary>
    public static string? NormalizeCode(string? code)
    {
        var value = code?.Trim().ToUpperInvariant();
        return value is { Length: >= 1 and <= 32 } && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-')
            ? value
            : null;
    }

    /// <summary>A PIN is digits only, within the configured length, and not one repeated digit (0000, 1111).</summary>
    public static bool IsAcceptablePin(string? pin, ShopFloorOptions options)
        => pin is { Length: > 0 }
           && pin.Length >= options.MinPinLength
           && pin.Length <= options.MaxPinLength
           && pin.All(char.IsAsciiDigit)
           && pin.Any(c => c != pin[0]);
}

/// <summary>
/// Verifies a shop-floor employee code and PIN against the accounts of one company. A wrong PIN counts toward the same
/// lock-out as a wrong password. Every denial costs one hash, so timing does not tell which codes exist.
/// </summary>
internal sealed class ShopFloorCredentialValidator<TUser>(
    UserManager<TUser> userManager,
    IOptions<ShopFloorOptions> options,
    ISecurityAuditLog? audit = null)
    : IShopFloorCredentialValidator
    where TUser : ModulusUser, new()
{
    private static string? s_dummyHash;

    public async Task<PasswordGrantResult> ValidateAsync(Guid tenantId, string employeeCode, string pin, CancellationToken ct = default)
    {
        var code = ShopFloorRules.NormalizeCode(employeeCode);
        if (code is null || string.IsNullOrEmpty(pin))
            return DenyAfterHashing(pin);

        var user = await userManager.Users.FirstOrDefaultAsync(u => u.TenantId == tenantId && u.EmployeeCode == code, ct);
        if (user is null)
        {
            Denied(tenantId, null, "unknown-code");
            return DenyAfterHashing(pin);
        }

        if (!user.IsActive || user.PinHash is null)
        {
            Denied(tenantId, user, user.IsActive ? "no-pin" : "account-disabled");
            return DenyAfterHashing(pin);
        }

        if (await userManager.IsLockedOutAsync(user))
        {
            Denied(tenantId, user, "locked-out");
            return DenyAfterHashing(pin);
        }

        var verdict = userManager.PasswordHasher.VerifyHashedPassword(user, user.PinHash, pin);
        if (verdict == PasswordVerificationResult.Failed)
        {
            await userManager.AccessFailedAsync(user);
            Denied(tenantId, user, "wrong-pin");
            return PasswordGrantResult.Denied();
        }

        await userManager.ResetAccessFailedCountAsync(user);
        if (verdict == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PinHash = userManager.PasswordHasher.HashPassword(user, pin);
            await userManager.UpdateAsync(user);
        }

        // The permission ceiling: only the configured floor roles ever reach a tablet token.
        var allowed = options.Value.AllowedRoles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var roles = (await userManager.GetRolesAsync(user)).Where(allowed.Contains).ToList();

        return new PasswordGrantResult
        {
            Success = true,
            Subject = await userManager.GetUserIdAsync(user),
            UserName = await userManager.GetUserNameAsync(user),
            TenantId = user.TenantId,
            Roles = roles,
            SecurityStamp = await userManager.GetSecurityStampAsync(user),
        };
    }

    private void Denied(Guid tenantId, TUser? user, string reason)
        => audit?.Record(new SecurityAuditEvent
        {
            Category = SecurityAuditCategories.Identity,
            Action = "signin.shop-floor",
            Outcome = SecurityAuditOutcomes.Denied,
            TenantId = tenantId,
            Actor = user?.Id.ToString(),
            Details = new Dictionary<string, string?> { ["reason"] = reason },
        });

    private PasswordGrantResult DenyAfterHashing(string? pin)
    {
        var hasher = userManager.PasswordHasher;
        var dummy = new TUser();
        s_dummyHash ??= hasher.HashPassword(dummy, Guid.NewGuid().ToString("N"));
        hasher.VerifyHashedPassword(dummy, s_dummyHash, pin ?? "");
        return PasswordGrantResult.Denied();
    }
}

/// <summary>Creates shop-floor accounts and sets their PINs. Registered by <c>AddModulusShopFloor</c>.</summary>
internal sealed class ShopFloorAccountService<TUser, TRole>(
    UserManager<TUser> userManager,
    RoleManager<TRole> roleManager,
    IOptions<ShopFloorOptions> options)
    : IShopFloorAccountService
    where TUser : ModulusUser, new()
    where TRole : ModulusRole
{
    public async Task<IdentityResult> CreateAsync(
        Guid tenantId,
        string employeeCode,
        string pin,
        string? firstName = null,
        string? lastName = null,
        IEnumerable<string>? roles = null,
        CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty)
            return Fail("TenantRequired", "A shop-floor account belongs to a company.");
        if (ShopFloorRules.NormalizeCode(employeeCode) is not { } code)
            return Fail("InvalidEmployeeCode", "An employee code is 1 to 32 letters, digits, '.', '_' or '-'.");
        if (!ShopFloorRules.IsAcceptablePin(pin, options.Value))
            return Fail("InvalidPin", PinRule());

        // Unique within the company, in code: a database index cannot ignore the null codes of ordinary accounts on every provider.
        if (await userManager.Users.AnyAsync(u => u.TenantId == tenantId && u.EmployeeCode == code, ct))
            return Fail("DuplicateEmployeeCode", "The employee code is already in use in this company.");

        var roleList = (roles ?? []).Distinct(StringComparer.Ordinal).ToList();
        foreach (var role in roleList)
        {
            if (!await roleManager.RoleExistsAsync(role))
                return Fail("RoleNotFound", $"The role '{role}' does not exist.");
        }

        var key = $"{tenantId:N}.{code}".ToLowerInvariant();
        var user = new TUser
        {
            UserName = $"emp.{key}",
            // Identity requires a unique email; this one is never mailed (the .invalid domain cannot resolve).
            Email = $"emp.{key}@shopfloor.invalid",
            EmailConfirmed = true,
            TenantId = tenantId,
            EmployeeCode = code,
            FirstName = firstName,
            LastName = lastName,
            IsActive = true,
        };
        user.PinHash = userManager.PasswordHasher.HashPassword(user, pin);

        var created = await userManager.CreateAsync(user);
        if (!created.Succeeded)
            return created;

        return roleList.Count == 0 ? created : await userManager.AddToRolesAsync(user, roleList);
    }

    public async Task<IdentityResult> SetPinAsync(Guid userId, string pin, CancellationToken ct = default)
    {
        if (!ShopFloorRules.IsAcceptablePin(pin, options.Value))
            return Fail("InvalidPin", PinRule());

        var user = await userManager.FindByIdAsync(userId.ToString("D", CultureInfo.InvariantCulture));
        if (user is null || user.EmployeeCode is null)
            return Fail("NotShopFloorAccount", "The account is not a shop-floor account.");

        user.PinHash = userManager.PasswordHasher.HashPassword(user, pin);
        var updated = await userManager.UpdateAsync(user);
        if (!updated.Succeeded)
            return updated;

        await userManager.ResetAccessFailedCountAsync(user);
        await userManager.SetLockoutEndDateAsync(user, null);
        return await userManager.UpdateSecurityStampAsync(user);
    }

    private string PinRule()
        => $"A PIN is {options.Value.MinPinLength} to {options.Value.MaxPinLength} digits and not one repeated digit.";

    private static IdentityResult Fail(string code, string description)
        => IdentityResult.Failed(new IdentityError { Code = code, Description = description });
}
