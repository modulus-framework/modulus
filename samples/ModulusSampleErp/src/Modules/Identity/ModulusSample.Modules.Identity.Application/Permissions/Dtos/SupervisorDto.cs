namespace ModulusSample.Modules.Identity.Application.Permissions.Dtos;

public sealed record SupervisorDto
{
    public Guid UserId { get; init; }
    public string FullName { get; init; } = string.Empty;
    public string UserName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public bool IsAvailable { get; init; }
}
