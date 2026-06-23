namespace Modulus.Data.Redis;

using StackExchange.Redis;

/// <summary>
/// Redis GEO commands for location tracking (e.g. driver positions).
/// </summary>
public sealed class RedisGeoService(IConnectionMultiplexer redis)
{
    private IDatabase Db => redis.GetDatabase();

    public Task AddAsync(
        string key, string member,
        double latitude, double longitude,
        CancellationToken ct = default)
        => Db.GeoAddAsync(key,
            new GeoEntry(longitude, latitude, member));

    public async Task<IReadOnlyList<GeoRadiusResult>> GetNearbyAsync(
        string key, double latitude, double longitude,
        double radius, GeoUnit unit = GeoUnit.Kilometers,
        int count = 10, CancellationToken ct = default)
    {
        var results = await Db.GeoRadiusAsync(
            key, longitude, latitude, radius, unit,
            count, Order.Ascending,
            GeoRadiusOptions.WithCoordinates |
            GeoRadiusOptions.WithDistance);
        return results ?? [];
    }

    public Task<double?> GetDistanceAsync(
        string key, string member1, string member2,
        GeoUnit unit = GeoUnit.Kilometers)
        => Db.GeoDistanceAsync(key, member1, member2, unit);

    public Task RemoveAsync(string key, string member)
        => Db.GeoRemoveAsync(key, member);
}