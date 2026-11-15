namespace Modulus.Messaging;

/// <summary>At most <see cref="Messages"/> deliveries per <see cref="Period"/> to one endpoint (FR-15). Excess messages wait.</summary>
public sealed record RateLimit(int Messages, TimeSpan Period)
{
    internal void Validate()
    {
        if (Messages <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Messages), "Must be positive.");
        }

        if (Period <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(Period), "Must be positive.");
        }
    }
}
