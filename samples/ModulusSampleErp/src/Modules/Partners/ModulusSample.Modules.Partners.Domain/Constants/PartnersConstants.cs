using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Partners.Domain.Constants;

public static class Schemas
{
    public const string Partners = "partners";
}

public static class PartnerTypes
{
    public const string Customer = "customer";
    public const string Supplier = "supplier";
    public const string Both = "both";
    public const string Distributor = "distributor";
    public const string Manufacturer = "manufacturer";
}

public static class PartnerStatuses
{
    public const string Active = "active";
    public const string Inactive = "inactive";
    public const string Prospective = "prospective";
    public const string Blocked = "blocked";
}

public static class ContactErrors
{
    public static readonly Error NotFound = Error.NotFound("Contact.NotFound", "Contact not found");
    public static readonly Error EmptyName = Error.Validation("Contact.EmptyName", "Contact name cannot be empty");
    public static readonly Error InvalidEmail = Error.Validation("Contact.InvalidEmail", "Invalid email format");
    public static readonly Error InvalidPhone = Error.Validation("Contact.InvalidPhone", "Invalid phone number format");
}

public static class AddressErrors
{
    public static readonly Error EmptyStreet = Error.Validation("Address.EmptyStreet", "Street address cannot be empty");
    public static readonly Error EmptyCity = Error.Validation("Address.EmptyCity", "City cannot be empty");
    public static readonly Error EmptyCountry = Error.Validation("Address.EmptyCountry", "Country cannot be empty");
    public static readonly Error InvalidPostalCode = Error.Validation("Address.InvalidPostalCode", "Invalid postal code format");
}

public static class PartnerErrors
{
    public static readonly Error NotFound = Error.NotFound("Partner.NotFound", "Partner not found");
    public static readonly Error DuplicateEmail = Error.Conflict("Partner.DuplicateEmail", "A partner with this email already exists");
    public static readonly Error DuplicateTaxId = Error.Conflict("Partner.DuplicateTaxId", "A partner with this tax ID already exists");
    public static readonly Error InvalidStatus = Error.Validation("Partner.InvalidStatus", "Invalid partner status");
    public static readonly Error InvalidType = Error.Validation("Partner.InvalidType", "Invalid partner type");
    public static readonly Error EmptyName = Error.Validation("Partner.EmptyName", "Partner name cannot be empty");
    public static readonly Error CannotDeleteActivePartner = Error.BusinessRule("Partner.CannotDeleteActivePartner", "Cannot delete an active partner");
    public static readonly Error EmptyContact = Error.Validation("Partner.EmptyContact", "Partner must have at least one contact");
    public static readonly Error EmptyAddress = Error.Validation("Partner.EmptyAddress", "Partner must have at least one address");
}