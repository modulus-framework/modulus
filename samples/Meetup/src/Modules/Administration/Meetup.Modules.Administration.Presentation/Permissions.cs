namespace Meetup.Modules.Administration.Presentation;

/// <summary>Permission names for the Administration module (colon-style so the
/// framework's permission-policy provider enforces them server-side).</summary>
public static class AdministrationPermissions
{
    public const string ProposeMeetingGroup = "administration:proposals:create";
    public const string DecideProposal = "administration:proposals:decide";
    public const string ViewProposals = "administration:proposals:view";
}
