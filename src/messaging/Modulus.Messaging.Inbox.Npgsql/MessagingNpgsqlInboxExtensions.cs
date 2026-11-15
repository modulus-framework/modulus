using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Modulus.Messaging.Outbox.Npgsql;

namespace Modulus.Messaging.Inbox.Npgsql;

public static partial class MessagingNpgsqlInboxExtensions
{
    /// <summary>
    /// Registers the PostgreSQL inbox. It records each handled message in the consume attempt's transaction, together with
    /// the consumer's writes and outgoing messages (see <see cref="MessagingNpgsqlOutboxExtensions.AddModulusMessagingNpgsqlUnitOfWork"/>).
    /// Call after <c>AddModulusMessaging</c>.
    /// </summary>
    public static IServiceCollection AddModulusMessagingNpgsqlInbox(this IServiceCollection services, string table = "messaging_inbox")
        => AddModulusMessagingNpgsqlInbox(services, table, createSchema: true);

    /// <summary>
    /// Registers the PostgreSQL inbox. With <paramref name="createSchema"/> the inbox table is created when the host starts; a
    /// migration that owns the schema passes false.
    /// </summary>
    public static IServiceCollection AddModulusMessagingNpgsqlInbox(this IServiceCollection services, string table, bool createSchema)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (!Identifier().IsMatch(table))
        {
            throw new ArgumentException("Table must be a plain SQL identifier.", nameof(table));
        }

        services.AddModulusMessagingNpgsqlUnitOfWork();
        services.AddScoped<IMessageInbox>(sp => new NpgsqlMessageInbox(sp.GetRequiredService<INpgsqlUnitOfWork>(), table));
        if (createSchema)
        {
            // The table must exist before the first consume attempt claims a record in it. A migration that owns the schema
            // turns this off.
            services.AddSingleton<IHostedService>(sp => new NpgsqlInboxSchemaService(sp.GetRequiredService<NpgsqlDataSource>(), table));
        }

        return services;
    }

    /// <summary>The SQL that creates the inbox table and its indexes, for a migration.</summary>
    public static string InboxSchemaScript(string table = "messaging_inbox") => NpgsqlInboxSql.Schema(table);

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,62}$")]
    private static partial Regex Identifier();
}
