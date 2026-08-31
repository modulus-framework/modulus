using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Identity.Domain.Errors;

public static class IdentityErrors
{
    public static class User
    {
        public static readonly Error PhoneAlreadyConfirmed = new(
            "User.PhoneAlreadyConfirmed",
            "Phone number is already confirmed",
            ErrorType.Validation);

        public static readonly Error NotFound = new(
            "User.NotFound",
            "User not found",
            ErrorType.NotFound);

        public static readonly Error InvalidCredentials = new(
            "User.InvalidCredentials",
            "Invalid email or password",
            ErrorType.Unauthorized);

        public static readonly Error EmailAlreadyExists = new(
            "User.EmailAlreadyExists",
            "Email already exists",
            ErrorType.Conflict);

        public static readonly Error UserNameAlreadyExists = new(
            "User.UserNameAlreadyExists",
            "Username already exists",
            ErrorType.Conflict);

        public static readonly Error UserTypeNotValid = new(
            "User.UserTypeNotValid",
            "User type is not valid",
            ErrorType.Validation);

        public static readonly Error NoRolesAssigned = new(
            "User.NoRolesAssigned",
            "No roles have been assigned to this user",
            ErrorType.BusinessRule);
    }

    public static class Role
    {
        public static readonly Error NotFound = new(
            "Role.NotFound",
            "Role not found",
            ErrorType.NotFound);

        public static readonly Error DuplicateName = new(
            "Role.DuplicateName",
            "Role name already exists",
            ErrorType.Conflict);

        public static readonly Error CannotDeleteSystemRole = new(
            "Role.CannotDeleteSystemRole",
            "Cannot delete system role",
            ErrorType.BusinessRule);
    }

    public static class Gdpr
    {
        public static readonly Error DataExport = new(
            "Gdpr.DataExport",
            "Data export error",
            ErrorType.Validation);

        public static readonly Error DeletionRequestExists = new(
            "Gdpr.DeletionRequestExists",
            "Account deletion request already exists",
            ErrorType.BusinessRule);

        public static readonly Error NoDeletionRequest = new(
            "Gdpr.NoDeletionRequest",
            "No active account deletion request found",
            ErrorType.NotFound);
    }

    public static class Permission
    {
        public static readonly Error NotFound = new(
            "Permission.NotFound",
            "Permission not found",
            ErrorType.NotFound);

        public static readonly Error InvalidCode = new(
            "Permission.InvalidCode",
            "Permission code is not valid",
            ErrorType.Validation);

        public static readonly Error CannotGrantPermissionNotHeld = new(
            "Permission.CannotGrantPermissionNotHeld",
            "You cannot grant a permission you do not hold yourself",
            ErrorType.Forbidden);

        public static readonly Error CannotModifySystemRole = new(
            "Permission.CannotModifySystemRole",
            "System roles cannot be modified through the API",
            ErrorType.Forbidden);
    }

    public static class Address
    {
        public static readonly Error NotFound = new(
            "Address.NotFound",
            "Address not found",
            ErrorType.NotFound);
    }
}
