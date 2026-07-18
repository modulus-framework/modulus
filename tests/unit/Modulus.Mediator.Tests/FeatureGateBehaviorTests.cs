using FluentAssertions;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Exceptions;
using Modulus.Core.Null;
using Modulus.Mediator.Abstractions.Attributes;
using Modulus.Mediator.Behaviors;
using Xunit;

namespace Modulus.Mediator.Tests;

/// <summary>
/// Proves <see cref="FeatureGateBehavior{TRequest,TResponse}"/>: a request with no
/// <see cref="RequireFeatureAttribute"/> passes through, a required feature that is
/// disabled short-circuits with <see cref="FeatureDisabledException"/> (before the handler
/// or any permission check runs), and an enabled feature proceeds. With the default
/// <see cref="NullFeatureGate"/> the guard is a no-op (blueprint §5.11, §14).
/// </summary>
[Trait("Category", "Unit")]
public sealed class FeatureGateBehaviorTests
{
    [RequireFeature("analytics.advanced")]
    private sealed record GatedCommand;

    private sealed record PlainCommand;

    [Fact]
    public async Task NoRequireFeatureAttribute_CallsNextDirectly()
    {
        var behavior = new FeatureGateBehavior<PlainCommand, string>(new StubGate(enabled: false));
        var called = false;

        var result = await behavior.HandleAsync(
            new PlainCommand(), () => { called = true; return Task.FromResult("ok"); }, default);

        result.Should().Be("ok");
        called.Should().BeTrue("a request with no feature requirement is never gated");
    }

    [Fact]
    public async Task DisabledFeature_ShortCircuits_BeforeHandler()
    {
        var behavior = new FeatureGateBehavior<GatedCommand, string>(new StubGate(enabled: false));
        var called = false;

        var act = () => behavior.HandleAsync(
            new GatedCommand(), () => { called = true; return Task.FromResult("ok"); }, default);

        (await act.Should().ThrowAsync<FeatureDisabledException>())
            .Which.Feature.Should().Be("analytics.advanced");
        called.Should().BeFalse("the handler must not run when the feature is unavailable");
    }

    [Fact]
    public async Task EnabledFeature_Proceeds()
    {
        var behavior = new FeatureGateBehavior<GatedCommand, string>(new StubGate(enabled: true));

        var result = await behavior.HandleAsync(
            new GatedCommand(), () => Task.FromResult("ran"), default);

        result.Should().Be("ran");
    }

    [Fact]
    public async Task NullFeatureGate_LeavesTheGuardANoOp()
    {
        var behavior = new FeatureGateBehavior<GatedCommand, string>(NullFeatureGate.Instance);

        var result = await behavior.HandleAsync(
            new GatedCommand(), () => Task.FromResult("ran"), default);

        result.Should().Be("ran", "until feature management is wired, every feature is enabled");
    }

    private sealed class StubGate(bool enabled) : IFeatureGate
    {
        public bool IsEnabled(string feature) => enabled;
    }
}
