namespace Modulus.EntityFrameworkCore.Isolation;

/// <summary>
/// The explicit opt-in for a bulk <c>ExecuteDelete</c> of soft-delete rows (<c>ISoftDelete</c>): it removes the rows for good,
/// which the soft delete exists to prevent. Without it, <see cref="TenantSqlGuardInterceptor"/> rejects the statement in every
/// context, the host included.
/// </summary>
/// <example>
/// <code>
/// using (HardDelete.Allow("Right to erasure request 4711"))
///     await db.Notes.Where(n => n.OwnerId == id).ExecuteDeleteAsync(ct);
/// </code>
/// </example>
/// <remarks>
/// The scope flows with the async context, so it covers the awaited calls inside the <c>using</c> block only. Each statement
/// it allows is logged and audited with the reason.
/// </remarks>
public static class HardDelete
{
    private static readonly AsyncLocal<string?> s_reason = new();

    /// <summary>The reason of the innermost open scope, or null when none is open.</summary>
    public static string? CurrentReason => s_reason.Value;

    /// <summary>Allows hard deletes of soft-delete rows until the returned scope is disposed.</summary>
    /// <param name="reason">Why the rows must be removed for good; logged with every statement it allows.</param>
    public static IDisposable Allow(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        var previous = s_reason.Value;
        s_reason.Value = reason;
        return new Scope(previous);
    }

    private sealed class Scope(string? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            s_reason.Value = previous;
        }
    }
}

/// <summary>
/// Thrown when a bulk <c>ExecuteDelete</c> would hard-delete soft-delete rows without a <see cref="HardDelete.Allow"/> scope.
/// </summary>
public sealed class HardDeleteException : InvalidOperationException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="tables">The soft-delete tables the statement references.</param>
    public HardDeleteException(IReadOnlyList<string> tables)
        : base($"A bulk delete would hard-delete soft-delete rows of {string.Join(", ", tables)}. Soft-delete them with SaveChanges, "
            + "or wrap a reviewed erasure in HardDelete.Allow(reason).")
    {
        Tables = tables;
    }

    /// <summary>The soft-delete tables the statement references.</summary>
    public IReadOnlyList<string> Tables { get; }
}
