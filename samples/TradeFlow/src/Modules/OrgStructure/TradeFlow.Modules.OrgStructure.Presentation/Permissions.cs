namespace TradeFlow.Modules.OrgStructure.Presentation;

internal static class Permissions
{
    public const string Read = "org:read";
    public const string Manage = "org:manage";
    public const string Admin = "org:admin";

    public static IReadOnlySet<string> AllSet { get; } = new HashSet<string>
    {
        Read,
        Manage,
        Admin
    };
}
