namespace Modulus.Caching.Redis.Tests;

using Xunit;

/// <summary>FusionCache over Redis 7 (see <see cref="FusionCacheContract"/>).</summary>
[Trait("Category", "Integration")]
public sealed class RedisFusionCacheTests : FusionCacheContract
{
    protected override string Image => "redis:7.0";
}
