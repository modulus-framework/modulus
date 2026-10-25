using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using Modulus.AspNetCore.RateLimiting;
using Xunit;

namespace Modulus.AspNetCore.Tests;

/// <summary>Past the partition cap a new key shares one overflow limiter: still limited, and the number of partitions stays bounded.</summary>
[Trait("Category", "Unit")]
public sealed class EvictableLimiterCapTests
{
    private static HttpContext Request(string key)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["k"] = key;
        return context;
    }

    [Fact]
    public void Keys_beyond_the_cap_share_one_limited_overflow_limiter()
    {
        var limiter = new EvictableFixedWindowLimiter(
            ctx => ctx.Request.Headers["k"].ToString(),
            () => new FixedWindowRateLimiterOptions { PermitLimit = 1, Window = TimeSpan.FromMinutes(10), QueueLimit = 0 },
            idleThreshold: TimeSpan.FromMinutes(10),
            sweepInterval: TimeSpan.FromMinutes(10));

        for (var i = 0; i < EvictableFixedWindowLimiter.MaxPartitions; i++)
            limiter.AttemptAcquire(Request($"k{i}"), 1).IsAcquired.Should().BeTrue();

        // The first key past the cap gets the overflow limiter's only permit; the second one shares that limiter and is refused.
        limiter.AttemptAcquire(Request("overflow-a"), 1).IsAcquired.Should().BeTrue();
        limiter.AttemptAcquire(Request("overflow-b"), 1).IsAcquired.Should().BeFalse("the overflow limiter is shared, so it is still limited");
    }
}
