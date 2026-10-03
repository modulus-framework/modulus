using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.MultiTenancy;
using Modulus.Storage;
using Xunit;

namespace Modulus.Platform.Tests;

[Trait("Category", "Unit")]
public sealed class TenantScopedFileStorageTests : IDisposable
{
    private static readonly Guid TenantA = Guid.Parse("a0000000-0000-0000-0000-00000000000a");
    private static readonly Guid TenantB = Guid.Parse("b0000000-0000-0000-0000-00000000000b");

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"modulus-tenant-storage-{Guid.NewGuid():N}");
    private readonly CurrentTenant _tenant = new();

    private IFileStorage Build(bool isolate)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentTenant>(_tenant);
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(configuration);
        services.AddFileStorage(configuration);
        services.Configure<StorageOptions>(o =>
        {
            o.BasePath = _root;
            o.IsolateTenants = isolate;
        });
        return services.BuildServiceProvider().GetRequiredService<IFileStorage>();
    }

    [Fact]
    public async Task Same_path_in_two_tenants_is_two_files()
    {
        var storage = Build(isolate: true);

        using (_tenant.Change(new TenantInfo(TenantA, "a")))
            await storage.UploadAsync("invoices/1.pdf", new MemoryStream("A"u8.ToArray()));

        using (_tenant.Change(new TenantInfo(TenantB, "b")))
        {
            (await storage.ExistsAsync("invoices/1.pdf")).Should().BeFalse("tenant B cannot see tenant A's file");
            await storage.UploadAsync("invoices/1.pdf", new MemoryStream("B"u8.ToArray()));
        }

        using (_tenant.Change(new TenantInfo(TenantA, "a")))
        {
            using var reader = new StreamReader(await storage.DownloadAsync("invoices/1.pdf"));
            (await reader.ReadToEndAsync()).Should().Be("A");
        }

        File.Exists(Path.Combine(_root, "tenants", TenantA.ToString("N"), "invoices", "1.pdf")).Should().BeTrue();
        File.Exists(Path.Combine(_root, "tenants", TenantB.ToString("N"), "invoices", "1.pdf")).Should().BeTrue();
    }

    [Theory]
    [InlineData("../b0000000000000000000000000000000b/x.txt")]
    [InlineData("docs/../../other/x.txt")]
    [InlineData("./x.txt")]
    [InlineData("/etc/passwd")]
    [InlineData("..\\x.txt")]
    public async Task Paths_that_could_leave_the_tenant_prefix_are_rejected(string path)
    {
        var storage = Build(isolate: true);
        using var _ = _tenant.Change(new TenantInfo(TenantA, "a"));

        var act = () => storage.ExistsAsync(path);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public void Host_context_uses_the_host_prefix()
    {
        var storage = (TenantScopedFileStorage)Build(isolate: true);
        using var _ = _tenant.Change(null);

        storage.Scope("x.txt").Should().Be("host/x.txt");
    }

    [Fact]
    public async Task No_tenant_resolved_fails_closed()
    {
        var storage = Build(isolate: true);

        var act = () => storage.ExistsAsync("x.txt");

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public void Isolation_off_keeps_the_provider_unwrapped()
        => Build(isolate: false).Should().BeOfType<LocalFileStorage>();

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
