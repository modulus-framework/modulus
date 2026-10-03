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
    /// <summary>PostgreSQL: grants, revokes and a <c>BEFORE UPDATE OR DELETE</c> / <c>TRUNCATE</c> trigger.</summary>
    public static string PostgreSql(string applicationRole, string schema = "public")
    {
        var role = Identifier(applicationRole);
        var table = $"\"{Identifier(schema)}\".\"{ModulusAuditDbContext.SecurityAuditTable}\"";
        return $"""
            REVOKE UPDATE, DELETE, TRUNCATE ON {table} FROM "{role}";
            GRANT SELECT, INSERT ON {table} TO "{role}";
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

    /// <summary>SQL Server: <c>DENY UPDATE, DELETE</c> and an <c>INSTEAD OF UPDATE, DELETE</c> trigger.</summary>
    public static string[] SqlServer(string applicationUser, string schema = "dbo")
    {
        var user = Identifier(applicationUser);
        var table = $"[{Identifier(schema)}].[{ModulusAuditDbContext.SecurityAuditTable}]";
        return
        [
            $"GRANT SELECT, INSERT ON {table} TO [{user}];",
            $"DENY UPDATE, DELETE ON {table} TO [{user}];",
            $"CREATE OR ALTER TRIGGER [{Identifier(schema)}].[modulus_security_audit_append_only] ON {table} "
                + "INSTEAD OF UPDATE, DELETE AS BEGIN THROW 51000, 'modulus_security_audit is append-only', 1; END;",
        ];
    }

    /// <summary>MySQL: grants and <c>BEFORE UPDATE</c> / <c>BEFORE DELETE</c> triggers (one statement each).</summary>
    public static string[] MySql(string applicationUser, string database, string host = "%")
    {
        var user = Identifier(applicationUser);
        var table = $"`{Identifier(database)}`.`{ModulusAuditDbContext.SecurityAuditTable}`";
        const string refuse = "SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'modulus_security_audit is append-only'";
        return
        [
            $"GRANT SELECT, INSERT ON {table} TO '{user}'@'{HostPattern(host)}';",
            $"DROP TRIGGER IF EXISTS `{Identifier(database)}`.`modulus_security_audit_no_update`;",
            $"CREATE TRIGGER `{Identifier(database)}`.`modulus_security_audit_no_update` BEFORE UPDATE ON {table} FOR EACH ROW {refuse};",
            $"DROP TRIGGER IF EXISTS `{Identifier(database)}`.`modulus_security_audit_no_delete`;",
            $"CREATE TRIGGER `{Identifier(database)}`.`modulus_security_audit_no_delete` BEFORE DELETE ON {table} FOR EACH ROW {refuse};",
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
