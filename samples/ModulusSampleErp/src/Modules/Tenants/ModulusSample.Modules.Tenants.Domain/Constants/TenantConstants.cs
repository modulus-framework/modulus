using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Tenants.Domain.Constants;

public static class Schemas
{
    public const string Tenants = "tenants";
}

public static class TenantErrors
{
    public static readonly Error NotFound = Error.NotFound("Tenant.NotFound", "Tenant not found");
    public static readonly Error DuplicateName = Error.Conflict("Tenant.DuplicateName", "A tenant with this name already exists");
    public static readonly Error DuplicateSubdomain = Error.Conflict("Tenant.DuplicateSubdomain", "A tenant with this subdomain already exists");
    public static readonly Error InvalidSubdomain = Error.Validation("Tenant.InvalidSubdomain", "Subdomain must be alphanumeric with hyphens only, 3-63 characters");
    public static readonly Error EmptyName = Error.Validation("Tenant.EmptyName", "Name cannot be empty");
    public static readonly Error NameTooLong = Error.Validation("Tenant.NameTooLong", "Name cannot exceed 200 characters");
    public static readonly Error ConnectionStringTooLong = Error.Validation("Tenant.ConnectionStringTooLong", "Connection string cannot exceed 2000 characters");
    public static readonly Error CannotDeleteActiveTenant = Error.BusinessRule("Tenant.CannotDeleteActiveTenant", "Cannot delete an active tenant");
    public static readonly Error AlreadyActive = Error.BusinessRule("Tenant.AlreadyActive", "Tenant is already active");
    public static readonly Error AlreadyInactive = Error.BusinessRule("Tenant.AlreadyInactive", "Tenant is already inactive");
}