namespace Modulus.Caching.Redis.Tests;

using Xunit;

/// <summary>FusionCache over Valkey 8, the Redis-compatible fork (see <see cref="FusionCacheContract"/>). Same contract as Redis: the L2, the backplane and the cache-name isolation.</summary>
[Trait("Category", "Integration")]
public sealed class ValkeyFusionCacheTests : FusionCacheContract
{
    protected override string Image => "valkey/valkey:8.0";
}
