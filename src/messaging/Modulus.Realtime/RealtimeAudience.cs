namespace Modulus.Realtime;

using System.Globalization;

/// <summary>
/// Who receives a message, always within the tenant it was published in. One recipient kind (everyone in the tenant,
/// certain users, or the followers of a topic), optionally narrowed by a permission each recipient must hold.
/// </summary>
/// <example>
/// <code>
/// RealtimeAudience.Permission("catalog:products:manage")            // everyone in the tenant who may manage products
/// RealtimeAudience.User(order.CustomerId)                           // one user, on every device
/// RealtimeAudience.Topic($"orders:{order.Id}").RequirePermission("orders:read")
/// </code>
/// </example>
public sealed record RealtimeAudience
{
    private RealtimeAudience(IReadOnlyList<string>? users, string? topic, string? permission)
    {
        Users = users;
        Topic = topic;
        RequiredPermission = permission;
    }

    /// <summary>Every connection of the tenant.</summary>
    public static RealtimeAudience Tenant { get; } = new(null, null, null);

    /// <summary>The users' connections (matched against the <c>sub</c> / name-identifier claim); null for other kinds.</summary>
    public IReadOnlyList<string>? Users { get; }

    /// <summary>Connections following this topic; null for other kinds.</summary>
    public string? Topic { get; }

    /// <summary>A permission (a <c>:</c> policy name) every recipient must hold; null for none.</summary>
    public string? RequiredPermission { get; }

    /// <summary>Everyone in the tenant who holds <paramref name="permission"/>.</summary>
    public static RealtimeAudience Permission(string permission) => Tenant.RequirePermission(permission);

    /// <summary>One user.</summary>
    public static RealtimeAudience User(string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        return new([userId], null, null);
    }

    /// <summary>One user.</summary>
    public static RealtimeAudience User(Guid userId) => User(userId.ToString("D", CultureInfo.InvariantCulture));

    /// <summary>Several users (an empty list reaches nobody).</summary>
    public static RealtimeAudience ForUsers(IEnumerable<string> userIds)
    {
        ArgumentNullException.ThrowIfNull(userIds);
        return new([.. userIds.Where(u => !string.IsNullOrWhiteSpace(u)).Distinct(StringComparer.Ordinal)], null, null);
    }

    /// <summary>The connections following <paramref name="topic"/> (see <see cref="RealtimeBuilder.AddTopic"/>).</summary>
    public static RealtimeAudience ForTopic(string topic)
    {
        RealtimeTopics.Validate(topic);
        return new(null, topic, null);
    }

    /// <summary>The same recipients, narrowed to those holding <paramref name="permission"/>.</summary>
    public RealtimeAudience RequirePermission(string permission)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);
        return new(Users, Topic, permission);
    }
}
