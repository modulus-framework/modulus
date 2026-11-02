namespace Modulus.Core.Time;

/// <summary>
/// Time-zone helpers keyed by IANA zone ids (for example <c>Europe/Berlin</c>, <c>America/Sao_Paulo</c>). Store instants
/// as UTC and keep the zone as a text id on the user or company; convert only at the edges. Zone data comes from the
/// operating system (tzdata on Linux and containers; .NET maps IANA ids on Windows too). A zone id that the runtime does
/// not know is refused, never silently replaced with UTC.
/// </summary>
public static class ZoneTime
{
    /// <summary>Returns true when <paramref name="zoneId"/> names a zone the runtime knows.</summary>
    public static bool IsValidZone(string? zoneId) => TryFindZone(zoneId, out _);

    /// <summary>Finds a zone by IANA id, or returns false for an unknown or empty id.</summary>
    public static bool TryFindZone(string? zoneId, out TimeZoneInfo zone)
    {
        zone = TimeZoneInfo.Utc;
        if (string.IsNullOrWhiteSpace(zoneId))
            return false;
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId.Trim());
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }

    /// <summary>Returns the zone for an IANA id.</summary>
    /// <exception cref="ArgumentException">The id is empty or unknown to the runtime.</exception>
    public static TimeZoneInfo GetZone(string zoneId)
    {
        if (!TryFindZone(zoneId, out var zone))
            throw new ArgumentException($"'{zoneId}' is not a known IANA time-zone id (for example Europe/Berlin).", nameof(zoneId));
        return zone;
    }

    /// <summary>Converts an instant to the wall-clock offset of <paramref name="zoneId"/>.</summary>
    public static DateTimeOffset ToZone(DateTimeOffset instant, string zoneId)
        => TimeZoneInfo.ConvertTime(instant, GetZone(zoneId));

    /// <summary>The calendar date an instant falls on in <paramref name="zoneId"/>.</summary>
    public static DateOnly LocalDate(DateTimeOffset instant, string zoneId)
        => DateOnly.FromDateTime(ToZone(instant, zoneId).DateTime);

    /// <summary>
    /// The first instant (UTC) of a calendar day in a zone. Handles daylight-saving edges: when midnight does not exist
    /// (the clock jumps over it) the day starts at the first valid instant after it; when midnight happens twice, the
    /// earlier instant is returned.
    /// </summary>
    public static DateTimeOffset StartOfDay(DateOnly date, string zoneId)
    {
        var zone = GetZone(zoneId);
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);

        if (zone.IsInvalidTime(local))
        {
            // Midnight is skipped by the change: the day begins at the moment the clock jumps forward.
            while (zone.IsInvalidTime(local))
                local = local.AddMinutes(1);
            return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
        }

        if (zone.IsAmbiguousTime(local))
        {
            // Midnight happens twice: take the earlier of the two instants.
            var candidates = zone.GetAmbiguousTimeOffsets(local)
                .Select(offset => new DateTimeOffset(local, offset).ToUniversalTime())
                .ToArray();
            return candidates.Min();
        }

        return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
    }

    /// <summary>
    /// The exclusive end of a calendar day in a zone: the start of the next day. Use it for range queries
    /// (<c>createdAt &gt;= start AND createdAt &lt; end</c>) so a day has no gap or overlap at the boundary.
    /// </summary>
    public static DateTimeOffset EndOfDayExclusive(DateOnly date, string zoneId)
        => StartOfDay(date.AddDays(1), zoneId);
}
