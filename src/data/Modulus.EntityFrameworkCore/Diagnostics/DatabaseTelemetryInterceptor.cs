namespace Modulus.EntityFrameworkCore.Diagnostics;

using System.Data.Common;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Modulus.Observability;

/// <summary>
/// Times every EF Core command of a module context and records it as a <c>Modulus.Database</c> span and the
/// <see cref="ModulusMeters.DatabaseCommandDuration"/> / <see cref="ModulusMeters.DatabaseCommandErrors"/> instruments.
/// </summary>
/// <remarks>
/// The statement text is never recorded: it can carry literal values. A span carries the provider name and the operation
/// keyword (<c>SELECT</c>, <c>INSERT</c>, ...), and a failure carries the exception type only, never its message.
/// Registered on every module context by <c>AddModuleDatabase</c>, so there is nothing to wire in an app.
/// </remarks>
internal sealed class DatabaseTelemetryInterceptor : DbCommandInterceptor
{
    internal static readonly DatabaseTelemetryInterceptor Instance = new();

    private static readonly ActivitySource s_source = new("Modulus.Database", "1.0.0");

    // Open commands, keyed by the command object itself: the Executing and Executed events of one command share the instance.
    private static readonly ConditionalWeakTable<DbCommand, CommandSpan> s_open = new();

    private sealed class CommandSpan(Activity? activity, string provider, string operation)
    {
        public Activity? Activity { get; } = activity;
        public string Provider { get; } = provider;
        public string Operation { get; } = operation;
        public long StartTimestamp { get; } = Stopwatch.GetTimestamp();
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Begin(command, eventData);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Begin(command, eventData);
        return result;
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
    {
        End(command, error: null);
        return ValueTask.FromResult(result);
    }

    public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
    {
        End(command, error: null);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Begin(command, eventData);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        Begin(command, eventData);
        return result;
    }

    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        End(command, error: null);
        return ValueTask.FromResult(result);
    }

    public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
    {
        End(command, error: null);
        return result;
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
    {
        Begin(command, eventData);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        Begin(command, eventData);
        return result;
    }

    public override ValueTask<object?> ScalarExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, object? result, CancellationToken cancellationToken = default)
    {
        End(command, error: null);
        return ValueTask.FromResult(result);
    }

    public override object? ScalarExecuted(DbCommand command, CommandExecutedEventData eventData, object? result)
    {
        End(command, error: null);
        return result;
    }

    // One failure hook covers readers, non-queries and scalars.
    public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        End(command, eventData.Exception);
        return Task.CompletedTask;
    }

    public override void CommandFailed(DbCommand command, CommandErrorEventData eventData) => End(command, eventData.Exception);

    private static void Begin(DbCommand command, CommandEventData eventData)
    {
        // EF Core does not always hand the context to command events (for example on some pooled or raw command paths); the
        // connection type still names the provider then.
        var provider = eventData.Context?.Database.ProviderName ?? command.Connection?.GetType().Name ?? "unknown";
        var operation = OperationOf(command.CommandText);
        var activity = s_source.StartActivity($"db {operation}", ActivityKind.Client);
        activity?.SetTag("db.system", provider);
        activity?.SetTag("db.operation", operation);
        s_open.AddOrUpdate(command, new CommandSpan(activity, provider, operation));
    }

    private static void End(DbCommand command, Exception? error)
    {
        if (!s_open.TryGetValue(command, out var span))
            return;

        s_open.Remove(command);
        var elapsed = Stopwatch.GetElapsedTime(span.StartTimestamp).TotalMilliseconds;
        var tags = new TagList { { "db.system", span.Provider }, { "db.operation", span.Operation } };
        ModulusMeters.DatabaseCommandDuration.Record(elapsed, tags);

        if (error is not null)
        {
            var errorType = error.GetType().FullName;
            ModulusMeters.DatabaseCommandErrors.Add(1,
                new TagList { { "db.system", span.Provider }, { "db.operation", span.Operation }, { "error.type", errorType } });
            span.Activity?.SetTag("error.type", errorType);
            span.Activity?.SetStatus(ActivityStatusCode.Error);
        }

        span.Activity?.Dispose();
    }

    /// <summary>The first keyword of the statement (<c>SELECT</c>, <c>INSERT</c>, ...), without its text.</summary>
    private static string OperationOf(string? commandText)
    {
        if (string.IsNullOrWhiteSpace(commandText))
            return "unknown";

        var text = commandText.AsSpan().TrimStart();
        var end = 0;
        while (end < text.Length && char.IsLetter(text[end]))
            end++;

        return end == 0 ? "unknown" : text[..end].ToString().ToUpperInvariant();
    }
}
