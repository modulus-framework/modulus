namespace ModulusSample.Modules.Identity.Application.Sessions.Dtos;

public sealed record SessionDto(
    Guid Id,
    DeviceInfoDto DeviceInfo,
    string? IpAddress,
    DateTime LoginTimeUtc,
    DateTime LastActivityTimeUtc,
    DateTime ExpiresAtUtc,
    bool IsCurrent);

public sealed record DeviceInfoDto(
    string Browser,
    string? BrowserVersion,
    string Os,
    string? OsVersion,
    string DeviceType);
