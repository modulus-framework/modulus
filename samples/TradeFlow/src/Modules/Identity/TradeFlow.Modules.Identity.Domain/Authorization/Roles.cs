namespace TradeFlow.Modules.Identity.Domain.Authorization;

public static class Roles
{
    public const string Admin = "Admin";
    public const string User = "User";

    public static IReadOnlyCollection<string> All { get; } =
    [
        Admin, User
    ];
}

public static class RolePriority
{
    public const int Admin = 1000;
    public const int User = 500;
}
