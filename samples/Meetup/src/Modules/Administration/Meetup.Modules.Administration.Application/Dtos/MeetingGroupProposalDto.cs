namespace Meetup.Modules.Administration.Application.Dtos;

/// <summary>Read model for a meeting group proposal.</summary>
public sealed record MeetingGroupProposalDto(
    Guid Id,
    string Name,
    string Description,
    string City,
    string CountryCode,
    string ProposerLogin,
    string Status,
    DateTime ProposedAt,
    DateTime? DecidedAt);
