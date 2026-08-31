using TradeFlow.Modules.Identity.Application.Abstractions.Identity;

namespace TradeFlow.Modules.Identity.Infrastructure.Authentication;

internal sealed class PasswordHasher : IPasswordHasher
{
    private const int WorkFactor = 12;

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        return BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);
    }

    public bool Verify(string password, string hash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        return hash != null && BCrypt.Net.BCrypt.Verify(password, hash);
    }
}
