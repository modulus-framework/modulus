using Modulus.Authorization.Fields;
using Modulus.Authorization.Resources;

namespace Modulus.Identity.Tests;

/// <summary>A field authorizer that refuses writes to the named fields, for the caller's clearance in these tests.</summary>
internal sealed class DenyFieldsAuthorizer(params string[] protectedFields) : IFieldAuthorizer
{
    public FieldMask MaskFor(Type type) => throw new NotSupportedException();

    public T Redact<T>(T projection) => throw new NotSupportedException();

    public Task<AccessDecision> AuthorizeWriteAsync(Type type, IEnumerable<string> attemptedFields, CancellationToken ct = default)
    {
        var denied = attemptedFields.Where(protectedFields.Contains).ToList();
        return Task.FromResult(denied.Count == 0
            ? AccessDecision.Allow()
            : AccessDecision.Deny(AccessReasonCodes.FieldProtected, $"write to {string.Join(", ", denied)} is not permitted"));
    }
}
