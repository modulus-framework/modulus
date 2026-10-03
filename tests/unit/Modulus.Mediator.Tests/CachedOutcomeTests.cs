using System.Text.Json;
using FluentAssertions;
using Modulus.Core.Abstractions.Exceptions;
using Modulus.Mediator.Behaviors;
using Xunit;

namespace Modulus.Mediator.Tests;

/// <summary>A cached query outcome survives the JSON round trip an L2 cache (Redis) puts it through.</summary>
[Trait("Category", "Unit")]
public sealed class CachedOutcomeTests
{
    public sealed record Item(Guid Id, string Name);

    [Fact]
    public async Task A_value_round_trips()
    {
        var item = new Item(Guid.NewGuid(), "Widget");

        var outcome = RoundTrip(await CachedOutcome<IReadOnlyList<Item>>.CaptureAsync(() => Task.FromResult<IReadOnlyList<Item>>([item])));

        outcome.Unwrap().Should().Equal(item);
    }

    [Fact]
    public async Task Domain_errors_round_trip_and_are_rethrown()
    {
        await Replays<NotFoundException>(new NotFoundException("Product 1 was not found."), e => e.Message.Should().Be("Product 1 was not found."));
        await Replays<ValidationException>(new ValidationException(["Name: required", "Price: positive"]), e => e.Errors.Should().Equal("Name: required", "Price: positive"));
        await Replays<ForbiddenException>(new ForbiddenException("catalog:read"), e => e.Permission.Should().Be("catalog:read"));
        await Replays<FeatureDisabledException>(new FeatureDisabledException("Beta"), e => e.Feature.Should().Be("Beta"));
        await Replays<ConflictException>(new ConflictException("taken"), e => e.Message.Should().Be("taken"));
        await Replays<UnauthorizedException>(new UnauthorizedException(), _ => { });
    }

    [Fact]
    public async Task Other_exceptions_are_not_captured()
    {
        var act = () => CachedOutcome<int>.CaptureAsync(() => throw new TimeoutException("database"));

        await act.Should().ThrowAsync<TimeoutException>();
    }

    private static async Task Replays<TException>(Exception error, Action<TException> check)
        where TException : Exception
    {
        var outcome = RoundTrip(await CachedOutcome<int>.CaptureAsync(() => throw error));

        var act = () => outcome.Unwrap();

        check(act.Should().Throw<TException>().Which);
    }

    private static CachedOutcome<T> RoundTrip<T>(CachedOutcome<T> outcome)
        => JsonSerializer.Deserialize<CachedOutcome<T>>(JsonSerializer.Serialize(outcome))
           ?? throw new InvalidOperationException("null outcome");
}
