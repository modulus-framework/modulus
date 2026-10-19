namespace Modulus.Secrets;

using Microsoft.Extensions.Hosting;

/// <summary>
/// Refuses to start outside Development when tenant databases are configured with a secret backend that keeps values in the
/// process or in configuration. Those backends exist for development and tests; a production tenant connection string must
/// come from a managed store such as OpenBao. A missing environment is treated as production.
/// </summary>
internal sealed class TenantSecretBackendCheck(ISecretBackend backend, IHostEnvironment? environment = null) : IHostedService
{
    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (environment?.IsDevelopment() == true)
            return Task.CompletedTask;

        if (backend is InMemorySecretStore or ConfigurationSecretStore)
        {
            throw new InvalidOperationException(
                $"Tenant databases read their connection strings through {backend.GetType().Name}, which is for development only. "
                + "Register a managed secret store (for example AddOpenBaoSecretStore) before AddModulusTenantDatabases.");
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
