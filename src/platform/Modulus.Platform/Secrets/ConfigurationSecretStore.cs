namespace Modulus.Secrets;

using Microsoft.Extensions.Configuration;
using Modulus.Core.Abstractions.Exceptions;
using Modulus.Core.Abstractions.Secrets;

/// <summary>
/// Development backend: reads secrets from configuration (user secrets or environment variables) under
/// <c>Secrets:Values:{path}</c>. Each key of the secret is a child, for example
/// <c>Secrets:Values:tenant-db/3f2c.../primary:connectionString</c>. Production uses the OpenBao provider instead.
/// </summary>
public sealed class ConfigurationSecretStore(IConfiguration configuration) : ISecretBackend
{
    private const string Root = "Secrets:Values";

    /// <inheritdoc />
    public Task<SecretValue> GetAsync(SecretReference reference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        cancellationToken.ThrowIfCancellationRequested();

        var fields = configuration.GetSection(Root).GetSection(reference.Path).GetChildren()
            .Where(c => c.Value is not null)
            .ToDictionary(c => c.Key, c => c.Value!, StringComparer.Ordinal);

        if (fields.Count == 0)
            throw new SecretNotFoundException("The configured secret was not found.");

        return Task.FromResult(new SecretValue(fields, version: 1));
    }
}
