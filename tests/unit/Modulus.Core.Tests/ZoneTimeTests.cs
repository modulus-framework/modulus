namespace Modulus.Core.Tests;

using FluentAssertions;
using Modulus.Core.Time;
using Xunit;

[Trait("Category", "Unit")]
public sealed class ZoneTimeTests
{
    [Theory]
    [InlineData("Europe/Berlin", true)]
    [InlineData("America/Sao_Paulo", true)]
    [InlineData("UTC", true)]
    [InlineData("Mars/Olympus", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Recognises_only_zones_the_runtime_knows(string? zone, bool expected)
    {
        ZoneTime.IsValidZone(zone).Should().Be(expected);
    }

    [Fact]
    public void Unknown_zone_is_refused_not_replaced_with_utc()
    {
        var act = () => ZoneTime.GetZone("Mars/Olympus");

        act.Should().Throw<ArgumentException>().WithParameterName("zoneId");
    }

    [Fact]
    public void Converts_an_instant_to_the_zone_offset()
    {
        var instant = new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

        ZoneTime.ToZone(instant, "Europe/Berlin").Offset.Should().Be(TimeSpan.FromHours(2)); // summer time
        ZoneTime.ToZone(instant, "America/Sao_Paulo").Offset.Should().Be(TimeSpan.FromHours(-3)); // no DST since 2019
    }

    [Fact]
    public void Local_date_can_differ_from_the_utc_date()
    {
        // 23:30 UTC on 31 Dec is already 1 Jan in Berlin (+01:00).
        var instant = new DateTimeOffset(2026, 12, 31, 23, 30, 0, TimeSpan.Zero);

        ZoneTime.LocalDate(instant, "Europe/Berlin").Should().Be(new DateOnly(2027, 1, 1));
        ZoneTime.LocalDate(instant, "UTC").Should().Be(new DateOnly(2026, 12, 31));
    }

    [Fact]
    public void Start_of_day_is_local_midnight_in_the_zone()
    {
        var start = ZoneTime.StartOfDay(new DateOnly(2026, 7, 1), "Europe/Berlin");

        start.Should().Be(new DateTimeOffset(2026, 6, 30, 22, 0, 0, TimeSpan.Zero)); // 00:00 CEST
    }

    [Fact]
    public void Start_of_day_handles_a_zone_whose_midnight_does_not_exist()
    {
        // America/Santiago switches from 00:00 to 01:00 on the first Sunday of September 2026 (clocks jump forward).
        var start = ZoneTime.StartOfDay(new DateOnly(2026, 9, 6), "America/Santiago");

        ZoneTime.ToZone(start, "America/Santiago").Should().Be(
            new DateTimeOffset(2026, 9, 6, 1, 0, 0, ZoneTime.GetZone("America/Santiago").GetUtcOffset(new DateTime(2026, 9, 6, 1, 0, 0))));
        ZoneTime.LocalDate(start, "America/Santiago").Should().Be(new DateOnly(2026, 9, 6));
    }

    [Fact]
    public void Day_ranges_are_contiguous_across_a_daylight_change()
    {
        var day = new DateOnly(2026, 10, 25); // Europe clocks go back: the day is 25 hours long
        var start = ZoneTime.StartOfDay(day, "Europe/Berlin");
        var end = ZoneTime.EndOfDayExclusive(day, "Europe/Berlin");

        (end - start).Should().Be(TimeSpan.FromHours(25));
        ZoneTime.EndOfDayExclusive(day, "Europe/Berlin").Should().Be(ZoneTime.StartOfDay(day.AddDays(1), "Europe/Berlin"));
    }
}
