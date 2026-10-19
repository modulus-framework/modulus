namespace Modulus.Secrets.OpenBao.Integration.Tests;

using System.Net.Http.Json;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Xunit;

/// <summary>
/// One OpenBao dev server per test class. It enables a KV v2 mount, writes the runtime and provisioning policies (the same
/// shapes as <c>deploy/openbao/*.hcl</c>, under the test mount), creates an AppRole identity for each, and seeds one tenant
/// secret. The identities' credentials are written to files, because the options read them from paths.
/// </summary>
public sealed class OpenBaoFixture : IAsyncLifetime
{
    /// <summary>The KV v2 mount the tests use.</summary>
    public const string Mount = "modulus-it";

    /// <summary>The namespace the policies allow.</summary>
    public const string PathPrefix = "tenant-db";

    /// <summary>The seeded tenant secret.</summary>
    public const string SeededTenant = "3f2c0000-0000-0000-0000-000000000001";

    private const string RootToken = "dev-root";
    private const int Port = 8200;

    private readonly List<string> _files = [];
    private IContainer _container = null!;
    private HttpClient _admin = null!;

    /// <summary>The address of the running server, for example <c>http://localhost:32768</c>.</summary>
    public string Address { get; private set; } = string.Empty;

    /// <summary>The runtime identity (read-only policy): AppRole role id file.</summary>
    public string RuntimeRoleIdPath { get; private set; } = string.Empty;

    /// <summary>The runtime identity: AppRole secret id file.</summary>
    public string RuntimeSecretIdPath { get; private set; } = string.Empty;

    /// <summary>The provisioning identity (create and update policy): AppRole role id file.</summary>
    public string ProvisionerRoleIdPath { get; private set; } = string.Empty;

    /// <summary>The provisioning identity: AppRole secret id file.</summary>
    public string ProvisionerSecretIdPath { get; private set; } = string.Empty;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        _container = new ContainerBuilder("openbao/openbao:2.1.0")
            .WithPortBinding(Port, assignRandomHostPort: true)
            // Dev mode listens on loopback only; bind the container's interface so the mapped port reaches it.
            .WithCommand("server", "-dev", $"-dev-root-token-id={RootToken}", $"-dev-listen-address=0.0.0.0:{Port}")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(
                request => request.ForPort(Port).ForPath("/v1/sys/health")))
            .Build();
        await _container.StartAsync();

        Address = $"http://{_container.Hostname}:{_container.GetMappedPublicPort(Port)}";
        _admin = new HttpClient { BaseAddress = new Uri(Address + "/") };
        _admin.DefaultRequestHeaders.Add("X-Vault-Token", RootToken);

        await BootstrapAsync();
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        _admin.Dispose();
        await _container.DisposeAsync();
        foreach (var file in _files.Where(File.Exists))
            File.Delete(file);
    }

    private async Task BootstrapAsync()
    {
        await PostAsync($"v1/sys/mounts/{Mount}", new { type = "kv", options = new { version = "2" } });

        await PutAsync("v1/sys/policies/acl/modulus-runtime-read", new
        {
            policy = $$"""
                path "{{Mount}}/data/{{PathPrefix}}/*" {
                  capabilities = ["read"]
                }
                """,
        });
        await PutAsync("v1/sys/policies/acl/modulus-provisioner", new
        {
            policy = $$"""
                path "{{Mount}}/data/{{PathPrefix}}/*" {
                  capabilities = ["create", "update", "read"]
                }
                path "{{Mount}}/metadata/{{PathPrefix}}/*" {
                  capabilities = ["read"]
                }
                """,
        });

        await PostAsync("v1/sys/auth/approle", new { type = "approle" });
        await PostAsync("v1/auth/approle/role/runtime", new { token_policies = "modulus-runtime-read", token_ttl = "1h" });
        await PostAsync("v1/auth/approle/role/provisioner", new { token_policies = "modulus-provisioner", token_ttl = "15m" });

        (RuntimeRoleIdPath, RuntimeSecretIdPath) = await CredentialFilesAsync("runtime");
        (ProvisionerRoleIdPath, ProvisionerSecretIdPath) = await CredentialFilesAsync("provisioner");

        await PostAsync($"v1/{Mount}/data/{PathPrefix}/{SeededTenant}/primary", new
        {
            data = new { connectionString = "Host=db.internal;Database=tenant_one" },
        });
    }

    private async Task<(string RoleIdPath, string SecretIdPath)> CredentialFilesAsync(string role)
    {
        var roleId = await ReadFieldAsync($"v1/auth/approle/role/{role}/role-id", "role_id");
        var secretId = await SecretIdAsync($"v1/auth/approle/role/{role}/secret-id");
        return (WriteFile(roleId), WriteFile(secretId));
    }

    private async Task<string> SecretIdAsync(string path)
    {
        using var response = await _admin.PostAsJsonAsync(path, new { });
        response.EnsureSuccessStatusCode();
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>();
        return document!.RootElement.GetProperty("data").GetProperty("secret_id").GetString()!;
    }

    private async Task<string> ReadFieldAsync(string path, string field)
    {
        using var response = await _admin.GetAsync(path);
        response.EnsureSuccessStatusCode();
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>();
        return document!.RootElement.GetProperty("data").GetProperty(field).GetString()!;
    }

    private async Task PostAsync(string path, object body)
    {
        using var response = await _admin.PostAsJsonAsync(path, body);
        response.EnsureSuccessStatusCode();
    }

    private async Task PutAsync(string path, object body)
    {
        using var response = await _admin.PutAsJsonAsync(path, body);
        response.EnsureSuccessStatusCode();
    }

    private string WriteFile(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"modulus-openbao-it-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, content);
        _files.Add(path);
        return path;
    }
}
