namespace ModulusSample.Modules.Identity.Infrastructure.Configuration;

public sealed class UsersSettings
{
    public AuthenticationSettings Authentication { get; set; } = new();
}

public sealed class AuthenticationSettings
{
    public int SessionTimeoutMinutes { get; set; } = 60;
}
