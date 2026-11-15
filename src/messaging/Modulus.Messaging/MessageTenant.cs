namespace Modulus.Messaging;

/// <summary>
/// The tenant of the current unit of work (FR-21). Messages sent while a tenant is entered carry it in
/// <see cref="MessagingHeaders.TenantId"/>, and a consumer runs with the tenant of the message it handles. Enter a
/// tenant in the scope that produces messages; a consumer's scope is set by the bus. The scope flows with async code,
/// like <c>CorrelationContext</c>.
/// </summary>
public static class MessageTenant
{
    private static readonly AsyncLocal<string?> Slot = new();

    /// <summary>The tenant in scope, or null when none is.</summary>
    public static string? Current => Slot.Value;

    /// <summary>Sets the tenant until the returned scope is disposed, then restores the previous one. Null clears it.</summary>
    public static IDisposable Enter(string? tenantId)
    {
        var previous = Slot.Value;
        Slot.Value = string.IsNullOrEmpty(tenantId) ? null : tenantId;
        return new Restore(previous);
    }

    private sealed class Restore(string? previous) : IDisposable
    {
        public void Dispose() => Slot.Value = previous;
    }
}
