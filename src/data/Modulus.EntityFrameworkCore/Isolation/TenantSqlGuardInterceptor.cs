namespace Modulus.EntityFrameworkCore.Isolation;

using System.Data.Common;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Entities;

/// <summary>
/// Rejects SQL that bypasses the tenant query filter while a tenant (or no tenant) is in scope: raw SQL
/// (<c>FromSql</c>, <c>SqlQuery</c>, <c>ExecuteSql</c>) and <c>IgnoreQueryFilters()</c> queries, when the
/// statement references a table of an <see cref="IHasTenantId"/> entity. The host context and a
/// <see cref="CrossTenantSql.Allow"/> scope pass. Registered on every module context by
/// <c>AddModuleDatabase</c>.
/// </summary>
/// <remarks>
/// <para>
/// This is the only database-side control on MySQL and SQLite (no row-level security); on PostgreSQL and SQL
/// Server it is defence in depth in front of the RLS policy.
/// </para>
/// <para>
/// An <c>IgnoreQueryFilters()</c> query is recognised by a tag added when EF Core compiles it, so the decision is
/// taken per execution, against the tenant in scope at that moment, even though the compiled query is cached.
/// Table references are found by name in the command text: a statement that reaches a tenant table through a
/// view or a function is not detected (RLS covers those on PostgreSQL and SQL Server).
/// </para>
/// </remarks>
public sealed class TenantSqlGuardInterceptor : DbCommandInterceptor, IQueryExpressionInterceptor
{
    /// <summary>The tag EF Core writes into the SQL of a query that ignores the query filters.</summary>
    public const string UnfilteredQueryTag = "modulus:unfiltered";

    /// <summary>
    /// The tag EF Core writes into the SQL of a query rooted in <c>FromSql</c> / <c>SqlQuery</c>. Such a query is
    /// composed (the filter wraps it) and reaches the database as a LINQ query, but its inner SQL is the caller's.
    /// </summary>
    public const string RawSqlQueryTag = "modulus:raw-sql";

    /// <summary>The shared instance (stateless apart from a per-model cache).</summary>
    public static TenantSqlGuardInterceptor Instance { get; } = new();

    private static readonly MethodInfo s_tagWith = typeof(EntityFrameworkQueryableExtensions)
        .GetMethods()
        .Single(m => m.Name == nameof(EntityFrameworkQueryableExtensions.TagWith) && m.GetParameters().Length == 2);

    private readonly ConditionalWeakTable<IModel, TenantTables> _tables = new();

    private TenantSqlGuardInterceptor()
    {
    }

    /// <inheritdoc />
    public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData)
        => eventData.Context is ModuleDbContext ? new UnfilteredTagger().Visit(queryExpression) : queryExpression;

    /// <inheritdoc />
    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Check(command, eventData);
        return result;
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Check(command, eventData);
        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        Check(command, eventData);
        return result;
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        Check(command, eventData);
        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        Check(command, eventData);
        return result;
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Check(command, eventData);
        return ValueTask.FromResult(result);
    }

    private void Check(DbCommand command, CommandEventData eventData)
    {
        if (eventData.Context is not ModuleDbContext context || context.TenantSession.IsHost)
            return;

        var text = command.CommandText;
        var origin = eventData.CommandSource switch
        {
            CommandSource.FromSqlQuery => "Raw SQL query",
            CommandSource.ExecuteSqlRaw => "Raw SQL command",
            _ when text.Contains(RawSqlQueryTag, StringComparison.Ordinal) => "Raw SQL query",
            _ when text.Contains(UnfilteredQueryTag, StringComparison.Ordinal) => "An IgnoreQueryFilters() query",
            _ => null,
        };
        if (origin is null)
            return;

        var tables = _tables.GetValue(context.Model, static model => new TenantTables(model)).Find(command.CommandText);
        if (tables.Count == 0)
            return;

        var logger = context.ContextServices.GetService<ILoggerFactory>()?.CreateLogger<TenantSqlGuardInterceptor>();
        var audit = context.ContextServices.GetService<ISecurityAuditLog>();
        if (CrossTenantSql.CurrentReason is { } reason)
        {
            logger?.LogWarning(
                "Cross-tenant SQL allowed on {Tables} for tenant {TenantId}: {Reason}",
                string.Join(", ", tables), context.TenantSession.TenantId, reason);
            Audit(audit, context, SecurityAuditOutcomes.Overridden, tables, origin, reason);
            return;
        }

        logger?.LogError(
            "{Origin} on {Tables} rejected for tenant {TenantId}", origin, string.Join(", ", tables), context.TenantSession.TenantId);
        Audit(audit, context, SecurityAuditOutcomes.Denied, tables, origin, reason: null);
        throw new CrossTenantSqlException(tables, origin);
    }

    // The statement text is never recorded: it may carry literal values (personal data, secrets).
    internal static void Audit(
        ISecurityAuditLog? audit, ModuleDbContext context, string outcome, IReadOnlyList<string> tables, string origin, string? reason)
    {
        if (audit is null)
            return;

        var details = new Dictionary<string, string?> { ["origin"] = origin, ["context"] = context.GetType().Name };
        if (reason is not null)
            details["reason"] = reason;
        audit.Record(new SecurityAuditEvent
        {
            Category = SecurityAuditCategories.Data,
            Action = "sql.cross-tenant",
            Outcome = outcome,
            TenantId = context.TenantSession.TenantId,
            Actor = context.ContextServices.GetService<ICurrentUser>()?.UserId?.ToString(),
            Target = string.Join(", ", tables),
            Details = details,
        });
    }

    /// <summary>The tables of a model's tenant entities, matched by name (quoted or bare) in command text.</summary>
    private sealed class TenantTables
    {
        private readonly Regex? _pattern;

        public TenantTables(IModel model)
        {
            var names = model.GetEntityTypes()
                .Where(e => typeof(IHasTenantId).IsAssignableFrom(e.ClrType))
                .Select(e => e.GetTableName())
                .OfType<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(n => n.Length)
                .ToList();
            if (names.Count > 0)
                _pattern = new Regex(
                    $"(?<![\\w$])({string.Join('|', names.Select(Regex.Escape))})(?![\\w$])",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                    TimeSpan.FromSeconds(1));
        }

        public IReadOnlyList<string> Find(string sql)
            => _pattern is null
                ? []
                : _pattern.Matches(sql).Select(m => m.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Tags every <c>IgnoreQueryFilters()</c> call (<see cref="UnfilteredQueryTag"/>) and every raw SQL root
    /// (<see cref="RawSqlQueryTag"/>) so the generated SQL says how the query was built.
    /// </summary>
    private sealed class UnfilteredTagger : ExpressionVisitor
    {
        // FromSqlQueryRootExpression / SqlQueryRootExpression are EF-internal types, so match them by name.
        protected override Expression VisitExtension(Expression node)
            => node is QueryRootExpression root
                && root.GetType().Name is "FromSqlQueryRootExpression" or "SqlQueryRootExpression"
                    ? Tag(node, root.ElementType, RawSqlQueryTag)
                    : base.VisitExtension(node);

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            var visited = base.VisitMethodCall(node);
            if (node.Method.DeclaringType != typeof(EntityFrameworkQueryableExtensions)
                || node.Method.Name != nameof(EntityFrameworkQueryableExtensions.IgnoreQueryFilters)
                || !node.Method.IsGenericMethod)
                return visited;

            return Tag(visited, node.Method.GetGenericArguments()[0], UnfilteredQueryTag);
        }

        private static MethodCallExpression Tag(Expression source, Type element, string tag)
            => Expression.Call(s_tagWith.MakeGenericMethod(element), source, Expression.Constant(tag));
    }
}
