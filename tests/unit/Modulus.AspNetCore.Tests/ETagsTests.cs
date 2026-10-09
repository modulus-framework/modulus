using FluentAssertions;
using Microsoft.Extensions.Primitives;
using Modulus.AspNetCore.Http;
using Xunit;

namespace Modulus.AspNetCore.Tests;

[Trait("Category", "Unit")]
public sealed class ETagsTests
{
    [Theory]
    [InlineData("abc", "\"abc\"")]
    [InlineData("\"abc\"", "\"abc\"")]
    public void Format_quotes_a_bare_value_once(string value, string expected)
        => ETags.Format(value).Should().Be(expected);

    [Theory]
    [InlineData("\"a\"", true)]
    [InlineData("\"x\", \"a\"", true)]
    [InlineData("W/\"a\"", true)]   // weak comparison for If-None-Match
    [InlineData("*", true)]
    [InlineData("\"b\"", false)]
    public void If_None_Match_uses_weak_comparison(string header, bool expected)
        => ETags.NoneMatchSatisfied(new StringValues(header), "a").Should().Be(expected);

    [Theory]
    [InlineData("\"a\"", true)]
    [InlineData("*", true)]
    [InlineData("W/\"a\"", false)]  // a weak tag never satisfies If-Match
    [InlineData("\"b\"", false)]
    public void If_Match_uses_strong_comparison(string header, bool expected)
        => ETags.IfMatchSatisfied(new StringValues(header), "a").Should().Be(expected);
}
