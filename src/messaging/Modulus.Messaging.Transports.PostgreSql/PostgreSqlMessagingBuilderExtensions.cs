namespace Modulus.Messaging.Transports.PostgreSql;

public static class PostgreSqlMessagingBuilderExtensions
{
    /// <summary>Uses PostgreSQL as the transport. Messages live in one queue table in the configured database.</summary>
    public static MessagingBusBuilder UsePostgreSql(this MessagingBusBuilder builder, Action<PostgreSqlTransportOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        var options = new PostgreSqlTransportOptions();
        configure(options);
        return builder.UseTransport(_ => new PostgreSqlTransport(options));
    }
}
