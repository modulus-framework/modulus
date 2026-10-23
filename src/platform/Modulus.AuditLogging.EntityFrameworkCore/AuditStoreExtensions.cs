namespace Modulus.AuditLogging.EntityFrameworkCore;

using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Modulus.AuditLogging.Security;

public static class AuditStoreExtensions
{
    /// <summary>
    /// Stores the business audit log and the security audit chains in <typeparamref name="TContext"/> (register the
    /// context itself with <c>AddDbContext</c>, ideally on its own connection string). Replaces the in-memory stores
    /// whichever order <c>AddModulusAuditLogging</c> / <c>AddModulusSecurityAudit</c> are called in, and registers the
    /// context as <see cref="DbContext"/> so <c>MigrateModulusDatabasesAsync</c> migrates it.
    /// </summary>
    public static IServiceCollection AddModulusAuditStore<TContext>(this IServiceCollection services)
        where TContext : ModulusAuditDbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(d => d.ServiceType == typeof(ModulusAuditDbContext)))
            return services;

        services.AddScoped<ModulusAuditDbContext>(sp => sp.GetRequiredService<TContext>());
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<TContext>());
        services.TryAddSingleton(TimeProvider.System);
        services.Replace(ServiceDescriptor.Scoped<IAuditLogStore, EfAuditLogStore>());
        services.Replace(ServiceDescriptor.Singleton<ISecurityAuditStore, EfSecurityAuditStore>());
        services.Replace(ServiceDescriptor.Singleton<Modulus.AuditLogging.Security.Retention.ISecurityAuditHolds, EfSecurityAuditHolds>());
        if (services.LastOrDefault(d => d.ServiceType == typeof(Modulus.AuditLogging.Security.Retention.ISecurityAuditArchive))?.ImplementationType
            == typeof(Modulus.AuditLogging.Security.Retention.NoSecurityAuditArchive))
        {
            services.RemoveAll<Modulus.AuditLogging.Security.Retention.ISecurityAuditArchive>();
        }
        services.TryAddSingleton<EfSecurityAuditArchive>();
        services.TryAddSingleton<Modulus.AuditLogging.Security.Retention.ISecurityAuditArchive>(sp => sp.GetRequiredService<EfSecurityAuditArchive>());
        return services;
    }
}

/// <summary>
/// Scripts that make <see cref="ModulusAuditDbContext.SecurityAuditTable"/> append-only: the application role may
/// read and insert, never update or delete, and a trigger refuses updates and deletes for every role (the table
/// owner included, until an administrator drops the trigger). Run them as the migration role after the table exists.
/// </summary>
public static partial class SecurityAuditDatabaseScripts
{
    /// <summary>
    /// PostgreSQL: grants, revokes and triggers. Without <paramref name="purgeRole"/> the table is append-only for every role. With it,
    /// that role alone may delete, and only entries at or below a checkpoint recorded for their chain (A11-1); updates and truncates
    /// stay refused for everyone. The checkpoint table is created by the context; the application role reads it.
    /// </summary>
    public static string PostgreSql(string applicationRole, string schema = "public", string? purgeRole = null)
    {
        var role = Identifier(applicationRole);
        var table = $"\"{Identifier(schema)}\".\"{ModulusAuditDbContext.SecurityAuditTable}\"";
        var checkpoints = $"\"{Identifier(schema)}\".\"{ModulusAuditDbContext.SecurityAuditCheckpointTable}\"";
        var holds = $"\"{Identifier(schema)}\".\"{ModulusAuditDbContext.SecurityAuditHoldTable}\"";
        var archive = $"\"{Identifier(schema)}\".\"{ModulusAuditDbContext.SecurityAuditArchiveTable}\"";
        if (purgeRole is null)
        {
            return $"""
                REVOKE UPDATE, DELETE, TRUNCATE ON {table} FROM "{role}";
                GRANT SELECT, INSERT ON {table} TO "{role}";
                GRANT SELECT, INSERT ON {holds} TO "{role}";
                GRANT SELECT, INSERT ON {archive} TO "{role}";
                CREATE OR REPLACE FUNCTION "{Identifier(schema)}".modulus_security_audit_append_only() RETURNS trigger
                    LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'modulus_security_audit is append-only'; END $$;
                DROP TRIGGER IF EXISTS modulus_security_audit_append_only ON {table};
                CREATE TRIGGER modulus_security_audit_append_only BEFORE UPDATE OR DELETE ON {table}
                    FOR EACH ROW EXECUTE FUNCTION "{Identifier(schema)}".modulus_security_audit_append_only();
                DROP TRIGGER IF EXISTS modulus_security_audit_no_truncate ON {table};
                CREATE TRIGGER modulus_security_audit_no_truncate BEFORE TRUNCATE ON {table}
                    FOR EACH STATEMENT EXECUTE FUNCTION "{Identifier(schema)}".modulus_security_audit_append_only();
                """;
        }

        var purger = Identifier(purgeRole);
        return $"""
            REVOKE UPDATE, DELETE, TRUNCATE ON {table} FROM "{role}";
            GRANT SELECT, INSERT ON {table} TO "{role}";
            GRANT SELECT, INSERT ON {holds} TO "{role}";
            GRANT SELECT ON {holds} TO "{purger}";
            GRANT SELECT, INSERT ON {archive} TO "{role}";
            GRANT SELECT, DELETE ON {table} TO "{purger}";
            REVOKE ALL ON {checkpoints} FROM "{role}";
            GRANT SELECT ON {checkpoints} TO "{role}";
            GRANT SELECT, INSERT, UPDATE ON {checkpoints} TO "{purger}";
            CREATE OR REPLACE FUNCTION "{Identifier(schema)}".modulus_security_audit_purge_only() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' AND current_user = '{purger}' AND EXISTS (
                        SELECT 1 FROM {checkpoints} c WHERE c."ChainId" = OLD."ChainId" AND OLD."Sequence" <= c."Sequence") THEN
                        RETURN OLD;
                    END IF;
                    RAISE EXCEPTION 'modulus_security_audit is append-only';
                END $$;
            DROP TRIGGER IF EXISTS modulus_security_audit_append_only ON {table};
            CREATE TRIGGER modulus_security_audit_append_only BEFORE UPDATE OR DELETE ON {table}
                FOR EACH ROW EXECUTE FUNCTION "{Identifier(schema)}".modulus_security_audit_purge_only();
            DROP TRIGGER IF EXISTS modulus_security_audit_no_truncate ON {table};
            CREATE TRIGGER modulus_security_audit_no_truncate BEFORE TRUNCATE ON {table}
                FOR EACH STATEMENT EXECUTE FUNCTION "{Identifier(schema)}".modulus_security_audit_purge_only();
            """;
    }

    /// <summary>
    /// SQL Server: <c>DENY UPDATE, DELETE</c> for the application user and an <c>INSTEAD OF UPDATE</c> trigger that refuses every update.
    /// With <paramref name="purgeUser"/>, that user alone may delete (A11-1): an <c>AFTER DELETE</c> trigger rolls the statement back
    /// unless the deleter is the purge user and every deleted entry is at or below the checkpoint of its chain. The purge user cannot
    /// update. TRUNCATE needs ALTER on the table, which neither user is granted.
    /// </summary>
    public static string[] SqlServer(string applicationUser, string schema = "dbo", string? purgeUser = null)
    {
        var user = Identifier(applicationUser);
        var table = $"[{Identifier(schema)}].[{ModulusAuditDbContext.SecurityAuditTable}]";
        var holds = $"[{Identifier(schema)}].[{ModulusAuditDbContext.SecurityAuditHoldTable}]";
        var archive = $"[{Identifier(schema)}].[{ModulusAuditDbContext.SecurityAuditArchiveTable}]";
        const string refuse = "THROW 51000, 'modulus_security_audit is append-only', 1;";
        var noUpdate = $"CREATE OR ALTER TRIGGER [{Identifier(schema)}].[modulus_security_audit_no_update] ON {table} "
            + $"INSTEAD OF UPDATE AS BEGIN {refuse} END;";

        if (purgeUser is null)
        {
            return
            [
                $"GRANT SELECT, INSERT ON {table} TO [{user}];",
                $"DENY UPDATE, DELETE ON {table} TO [{user}];",
                $"GRANT SELECT, INSERT ON {holds} TO [{user}];",
                $"GRANT SELECT, INSERT ON {archive} TO [{user}];",
                $"CREATE OR ALTER TRIGGER [{Identifier(schema)}].[modulus_security_audit_append_only] ON {table} "
                    + "INSTEAD OF UPDATE, DELETE AS BEGIN THROW 51000, 'modulus_security_audit is append-only', 1; END;",
            ];
        }

        var purger = Identifier(purgeUser);
        var checkpoints = $"[{Identifier(schema)}].[{ModulusAuditDbContext.SecurityAuditCheckpointTable}]";
        return
        [
            $"GRANT SELECT, INSERT ON {table} TO [{user}];",
            $"DENY UPDATE, DELETE ON {table} TO [{user}];",
            $"GRANT SELECT, INSERT ON {holds} TO [{user}];",
            $"GRANT SELECT ON {holds} TO [{purger}];",
            $"GRANT SELECT, INSERT ON {archive} TO [{user}];",
            $"GRANT SELECT, DELETE ON {table} TO [{purger}];",
            $"DENY UPDATE ON {table} TO [{purger}];",
            $"GRANT SELECT ON {checkpoints} TO [{user}];",
            $"DENY INSERT, UPDATE, DELETE ON {checkpoints} TO [{user}];",
            $"GRANT SELECT, INSERT, UPDATE ON {checkpoints} TO [{purger}];",
            noUpdate,
            $"CREATE OR ALTER TRIGGER [{Identifier(schema)}].[modulus_security_audit_purge_only] ON {table} AFTER DELETE AS BEGIN "
                + $"IF USER_NAME() <> '{purger}' OR EXISTS (SELECT 1 FROM deleted d WHERE NOT EXISTS "
                + $"(SELECT 1 FROM {checkpoints} c WHERE c.[ChainId] = d.[ChainId] AND d.[Sequence] <= c.[Sequence])) "
                + $"BEGIN {refuse} END END;",
        ];
    }

    /// <summary>
    /// MySQL: grants and <c>BEFORE UPDATE</c> / <c>BEFORE DELETE</c> triggers. Without <paramref name="purgeUser"/> the table is append-only
    /// for every user. With it, that user alone may delete, and only entries at or below the checkpoint of their chain (A11-1); the
    /// application user keeps reading the checkpoint and cannot write it. The trigger checks the connected user (<c>USER()</c>), not the
    /// trigger's definer. Updates stay refused for everyone; MySQL has no TRUNCATE trigger, but TRUNCATE needs DROP on the table, which
    /// neither user is granted.
    /// </summary>
    public static string[] MySql(string applicationUser, string database, string host = "%", string? purgeUser = null)
    {
        var user = Identifier(applicationUser);
        var db = Identifier(database);
        var table = $"`{db}`.`{ModulusAuditDbContext.SecurityAuditTable}`";
        var holds = $"`{db}`.`{ModulusAuditDbContext.SecurityAuditHoldTable}`";
        var archive = $"`{db}`.`{ModulusAuditDbContext.SecurityAuditArchiveTable}`";
        const string refuse = "SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'modulus_security_audit is append-only'";
        var hosts = HostPattern(host);

        if (purgeUser is null)
        {
            return
            [
                $"GRANT SELECT, INSERT ON {table} TO '{user}'@'{hosts}';",
                $"GRANT SELECT, INSERT ON {holds} TO '{user}'@'{hosts}';",
                $"GRANT SELECT, INSERT ON {archive} TO '{user}'@'{hosts}';",
                $"DROP TRIGGER IF EXISTS `{db}`.`modulus_security_audit_no_update`;",
                $"CREATE TRIGGER `{db}`.`modulus_security_audit_no_update` BEFORE UPDATE ON {table} FOR EACH ROW {refuse};",
                $"DROP TRIGGER IF EXISTS `{db}`.`modulus_security_audit_no_delete`;",
                $"CREATE TRIGGER `{db}`.`modulus_security_audit_no_delete` BEFORE DELETE ON {table} FOR EACH ROW {refuse};",
            ];
        }

        var purger = Identifier(purgeUser);
        var checkpoints = $"`{db}`.`{ModulusAuditDbContext.SecurityAuditCheckpointTable}`";
        return
        [
            $"GRANT SELECT, INSERT ON {table} TO '{user}'@'{hosts}';",
            $"GRANT SELECT, INSERT ON {holds} TO '{user}'@'{hosts}';",
            $"GRANT SELECT ON {holds} TO '{purger}'@'{hosts}';",
            $"GRANT SELECT, INSERT ON {archive} TO '{user}'@'{hosts}';",
            $"GRANT SELECT ON {checkpoints} TO '{user}'@'{hosts}';",
            $"GRANT SELECT, DELETE ON {table} TO '{purger}'@'{hosts}';",
            $"GRANT SELECT, INSERT, UPDATE ON {checkpoints} TO '{purger}'@'{hosts}';",
            $"DROP TRIGGER IF EXISTS `{db}`.`modulus_security_audit_no_update`;",
            $"CREATE TRIGGER `{db}`.`modulus_security_audit_no_update` BEFORE UPDATE ON {table} FOR EACH ROW {refuse};",
            $"DROP TRIGGER IF EXISTS `{db}`.`modulus_security_audit_no_delete`;",
            $"CREATE TRIGGER `{db}`.`modulus_security_audit_no_delete` BEFORE DELETE ON {table} FOR EACH ROW BEGIN "
                + $"IF SUBSTRING_INDEX(USER(), '@', 1) <> '{purger}' OR NOT EXISTS (SELECT 1 FROM {checkpoints} c "
                + "WHERE c.`ChainId` = OLD.`ChainId` AND OLD.`Sequence` <= c.`Sequence`) THEN "
                + $"{refuse}; END IF; END;",
        ];
    }

    private static string Identifier(string value)
        => value is not null && IdentifierPattern().IsMatch(value)
            ? value
            : throw new ArgumentException($"'{value}' is not a plain identifier (letters, digits, underscore).", nameof(value));

    private static string HostPattern(string value)
        => value is not null && HostPatternRegex().IsMatch(value)
            ? value
            : throw new ArgumentException($"'{value}' is not a valid MySQL host pattern.", nameof(value));

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,62}$")]
    private static partial Regex IdentifierPattern();

    [GeneratedRegex("^[A-Za-z0-9_.%:-]{1,255}$")]
    private static partial Regex HostPatternRegex();
}

/// <summary>
/// The context the retention purge runs on when a purge role is configured (A11-1). It is the audit context on a connection that
/// logs in as the purge role, so the database, not the application, decides whether a delete is allowed.
/// </summary>
public sealed class ModulusAuditPurgeDbContext(DbContextOptions<ModulusAuditPurgeDbContext> options)
    : ModulusAuditDbContext(options);

public static class AuditPurgeRoleExtensions
{
    /// <summary>
    /// Registers the purge context on the purge role's connection (<paramref name="configure"/> sets the provider and connection string).
    /// Without it <c>PurgeAsync</c> refuses: the application role cannot delete audit rows.
    /// </summary>
    public static IServiceCollection AddModulusAuditPurgeRole(this IServiceCollection services, Action<DbContextOptionsBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        services.AddDbContext<ModulusAuditPurgeDbContext>(configure);
        return services;
    }
}
