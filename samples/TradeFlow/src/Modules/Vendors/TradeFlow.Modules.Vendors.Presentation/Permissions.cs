namespace TradeFlow.Modules.Vendors.Presentation;

internal static class Permissions
{
    public const string Read = "vendor:read";
    public const string Manage = "vendor:manage";
    public const string Approve = "vendor:approve";

    public static IReadOnlySet<string> AllSet { get; } = new HashSet<string>
    {
        Read,
        Manage,
        Approve
    };
}
