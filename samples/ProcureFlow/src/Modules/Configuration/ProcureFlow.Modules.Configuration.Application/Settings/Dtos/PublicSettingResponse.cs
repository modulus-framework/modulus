using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Configuration.Application.Settings.Dtos;

public sealed record PublicSettingResponse(
    string Key,
    string Value,
    string Category);
