using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Settings.Application.Settings.Dtos;

public sealed record PublicSettingResponse(
    string Key,
    string Value,
    string Category);