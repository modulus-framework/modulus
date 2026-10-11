using FluentAssertions;
using Microsoft.Extensions.Options;
using Modulus.Storage;
using Xunit;

namespace Modulus.Platform.Tests;

[Trait("Category", "Unit")]
public sealed class LocalFileStorageTests
{
    private static LocalFileStorage Create(string? basePath)
        => new(Options.Create(basePath is null ? new StorageOptions() : new StorageOptions { BasePath = basePath }));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task An_unconfigured_base_path_falls_back_to_the_default_root_instead_of_throwing(string? basePath)
    {
        // Regression: StorageOptions.BasePath defaults to "", so `BasePath ?? "storage"` never applied and
        // Path.GetFullPath("") threw "The path is empty" on the first file operation of an unconfigured host.
        var storage = Create(basePath);

        var exists = await storage.ExistsAsync($"missing-{Guid.NewGuid():N}.txt");

        exists.Should().BeFalse();
    }

    [Fact]
    public async Task A_sibling_directory_differing_only_in_case_is_outside_the_root_on_a_case_sensitive_file_system()
    {
        // Only meaningful where "store" and "STORE" are different directories (Linux, most containers).
        if (OperatingSystem.IsWindows())
            return;
        var root = Path.Combine(Path.GetTempPath(), "modulus-case-" + Guid.NewGuid().ToString("N"));
        var store = Path.Combine(root, "store");
        var sibling = Path.Combine(root, "STORE");
        Directory.CreateDirectory(store);
        Directory.CreateDirectory(sibling);
        try
        {
            var storage = Create(store);

            var act = () => storage.UploadAsync("../STORE/escape.txt", new MemoryStream([1]));

            (await act.Should().ThrowAsync<ArgumentException>()).Which.Message.Should().Contain("escapes");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task A_configured_base_path_round_trips_a_file()
    {
        var root = Path.Combine(Path.GetTempPath(), $"modulus-storage-{Guid.NewGuid():N}");
        try
        {
            var storage = Create(root);

            await storage.UploadAsync("docs/a.txt", new MemoryStream("hello"u8.ToArray()));

            (await storage.ExistsAsync("docs/a.txt")).Should().BeTrue();
            using var reader = new StreamReader(await storage.DownloadAsync("docs/a.txt"));
            (await reader.ReadToEndAsync()).Should().Be("hello");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task A_path_that_escapes_the_root_is_rejected()
    {
        var storage = Create(Path.Combine(Path.GetTempPath(), $"modulus-storage-{Guid.NewGuid():N}"));

        var act = () => storage.ExistsAsync("../outside.txt");

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
