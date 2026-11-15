using System.Security.Cryptography;
using System.Text;

namespace Modulus.Messaging.Sagas;

/// <summary>
/// The storage key of a saga instance (FR-21). Outside a tenant it is the correlation key itself. Inside one it is a
/// Guid derived from the tenant and the correlation key, so two tenants that use the same key never share an instance.
/// The saga's <see cref="SagaInstance.CorrelationId"/> holds this derived key.
/// </summary>
public static class SagaKeys
{
    public static Guid ForTenant(string? tenantId, Guid correlationKey)
    {
        if (string.IsNullOrEmpty(tenantId))
        {
            return correlationKey;
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(tenantId + "\n" + correlationKey.ToString("N")));
        return new Guid(hash.AsSpan(0, 16));
    }
}
