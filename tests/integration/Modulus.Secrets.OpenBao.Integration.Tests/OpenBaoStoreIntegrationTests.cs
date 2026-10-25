namespace Modulus.Secrets.OpenBao.Integration.Tests;

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Modulus.Core.Abstractions.Exceptions;
using Modulus.Core.Abstractions.Secrets;
using Xunit;

/// <summary>
/// Runs the public registration (<c>AddOpenBaoSecretStore</c> / <c>AddOpenBaoSecretWriter</c>) against a real OpenBao server,
/// with AppRole identities that carry the shipped policy shapes.
/// </summary>
[Trait("Category", "Integration")]
public sealed class OpenBaoStoreIntegrationTests(OpenBaoFixture openBao) : IClassFixture<OpenBaoFixture>
{
    private sealed record Identity(string Role, string RoleIdPath, string SecretIdPath);

    private Identity Runtime => new("runtime", openBao.RuntimeRoleIdPath, openBao.RuntimeSecretIdPath);

    private Identity Provisioner => new("provisioner", openBao.ProvisionerRoleIdPath, openBao.ProvisionerSecretIdPath);

    private static SecretReference TenantPrimary(string tenant) => SecretReference.Parse($"{OpenBaoFixture.PathPrefix}/{tenant}/primary");

    private static string NewTenant() => Guid.NewGuid().ToString("D");

    /// <summary>Builds a host the way an application does: runtime store, and a writer only when one is given.</summary>
    private static ServiceProvider Host(string address, Identity runtime, Identity? writer = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["Secrets:OpenBao:Address"] = address,
            ["Secrets:OpenBao:Mount"] = OpenBaoFixture.Mount,
            ["Secrets:OpenBao:PathPrefix"] = OpenBaoFixture.PathPrefix,
            ["Secrets:OpenBao:AllowInsecureHttp"] = "true",
            ["Secrets:OpenBao:RequestTimeout"] = "00:00:05",
        };
        AddIdentity(values, "Secrets:OpenBao:Auth", runtime);
        if (writer is not null)
        {
            values["Secrets:RuntimeReadOnly"] = "false";
            AddIdentity(values, "Secrets:OpenBao:Writer", writer);
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        // Plain http is accepted only by a Development host (see OpenBaoOptionsValidator).
        services.AddSingleton<Microsoft.Extensions.Hosting.IHostEnvironment>(new DevelopmentEnvironment());
        services.AddOpenBaoSecretStore(configuration);
        if (writer is not null)
            services.AddOpenBaoSecretWriter(configuration);
        return services.BuildServiceProvider();
    }

    private sealed class DevelopmentEnvironment : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "openbao-integration";
        public string ContentRootPath { get; set; } = ".";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    private static void AddIdentity(Dictionary<string, string?> values, string section, Identity identity)
    {
        values[$"{section}:Method"] = "AppRole";
        values[$"{section}:Role"] = identity.Role;
        values[$"{section}:RoleIdPath"] = identity.RoleIdPath;
        values[$"{section}:SecretIdPath"] = identity.SecretIdPath;
    }

    [Fact]
    public async Task AppRoleLogin_ReadsTheSeededSecret()
    {
        await using var host = Host(openBao.Address, Runtime);
        var backend = host.GetRequiredService<ISecretBackend>();

        var secret = await backend.GetAsync(TenantPrimary(OpenBaoFixture.SeededTenant), CancellationToken.None);

        secret.Version.Should().Be(1);
        secret.Get("connectionString").Should().Be("Host=db.internal;Database=tenant_one");
    }

    [Fact]
    public async Task MissingSecret_IsNotFound()
    {
        await using var host = Host(openBao.Address, Runtime);
        var backend = host.GetRequiredService<ISecretBackend>();

        Func<Task> act = () => backend.GetAsync(TenantPrimary(NewTenant()), CancellationToken.None);

        await act.Should().ThrowAsync<SecretNotFoundException>();
    }

    [Fact]
    public async Task RuntimeIdentity_CannotWriteSecrets()
    {
        // The read-only policy has no create capability, so OpenBao refuses the write for the runtime identity.
        await using var host = Host(openBao.Address, Runtime, writer: Runtime);
        var writer = host.GetRequiredService<ISecretWriter>();

        Func<Task> act = () => writer.WriteAsync(
            TenantPrimary(NewTenant()),
            new Dictionary<string, string> { ["connectionString"] = "Host=denied" },
            expectedVersion: null,
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Provisioner_CheckAndSetRejectsAStaleVersion()
    {
        await using var host = Host(openBao.Address, Runtime, Provisioner);
        var writer = host.GetRequiredService<ISecretWriter>();
        var reference = TenantPrimary(NewTenant());

        var created = await writer.WriteAsync(reference, new Dictionary<string, string> { ["connectionString"] = "a" }, expectedVersion: 0, CancellationToken.None);
        var updated = await writer.WriteAsync(reference, new Dictionary<string, string> { ["connectionString"] = "b" }, expectedVersion: 1, CancellationToken.None);

        created.Should().Be(1);
        updated.Should().Be(2);
        Func<Task> stale = () => writer.WriteAsync(reference, new Dictionary<string, string> { ["connectionString"] = "c" }, expectedVersion: 1, CancellationToken.None);
        await stale.Should().ThrowAsync<ConflictException>();
        (await writer.GetCurrentVersionAsync(reference, CancellationToken.None)).Should().Be(2);
    }

    [Fact]
    public async Task RuntimeRead_SeesAProvisionerWrite()
    {
        await using var runtime = Host(openBao.Address, Runtime);
        await using var provisioner = Host(openBao.Address, Runtime, Provisioner);
        var reference = TenantPrimary(NewTenant());

        await provisioner.GetRequiredService<ISecretWriter>().WriteAsync(
            reference, new Dictionary<string, string> { ["connectionString"] = "Host=db2" }, expectedVersion: 0, CancellationToken.None);
        var secret = await runtime.GetRequiredService<ISecretBackend>().GetAsync(reference, CancellationToken.None);

        secret.Version.Should().Be(1);
        secret.Get("connectionString").Should().Be("Host=db2");
    }

    [Fact]
    public async Task UnreachableServer_IsUnavailable()
    {
        await using var host = Host("http://127.0.0.1:1", Runtime);
        var backend = host.GetRequiredService<ISecretBackend>();

        Func<Task> act = () => backend.GetAsync(TenantPrimary(OpenBaoFixture.SeededTenant), CancellationToken.None);

        await act.Should().ThrowAsync<SecretStoreUnavailableException>();
    }
}
