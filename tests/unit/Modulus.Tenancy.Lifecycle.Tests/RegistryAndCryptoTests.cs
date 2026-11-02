namespace Modulus.Tenancy.Lifecycle.Tests;

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Modulus.Tenancy.Lifecycle.Export;
using Xunit;

[Trait("Category", "Unit")]
public sealed class RegistryAndCryptoTests
{
    [Theory]
    [InlineData("Owned dataset", TenantDataCategory.Owned, RetentionClass.OperationalAudit, "owned")]
    [InlineData("Retained without a class", TenantDataCategory.Retained, RetentionClass.None, "needs a retention class")]
    [InlineData("Global with a class", TenantDataCategory.Global, RetentionClass.Financial, "never deleted")]
    public void Registry_refuses_categories_that_contradict_their_retention(string name, TenantDataCategory category, RetentionClass retention, string reason)
    {
        var act = () => TenantDataRegistry.Build([(typeof(StubHandler), new StubHandler("M", new TenantDataset("m." + name.Replace(' ', '-').ToLowerInvariant(), category, retention)))]);

        act.Should().Throw<InvalidOperationException>().WithMessage("*" + reason + "*");
    }

    [Theory]
    [InlineData("Bad Name")]
    [InlineData("Upper.Case")]
    [InlineData("")]
    public void Registry_refuses_dataset_names_that_are_not_safe_entry_names(string name)
    {
        var act = () => TenantDataRegistry.Build([(typeof(StubHandler), new StubHandler("M", new TenantDataset(name, TenantDataCategory.Owned)))]);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Registry_refuses_a_dataset_declared_twice()
    {
        var act = () => TenantDataRegistry.Build(
        [
            (typeof(StubHandler), new StubHandler("A", new TenantDataset("shared.name", TenantDataCategory.Owned))),
            (typeof(StubHandler), new StubHandler("B", new TenantDataset("shared.name", TenantDataCategory.Owned))),
        ]);

        act.Should().Throw<InvalidOperationException>().WithMessage("*declared twice*");
    }

    [Fact]
    public void Registry_keeps_registration_order()
    {
        var registry = TenantDataRegistry.Build(
        [
            (typeof(StubHandler), new StubHandler("A", new TenantDataset("a.one", TenantDataCategory.Owned), new TenantDataset("a.two", TenantDataCategory.Owned))),
            (typeof(StubHandler), new StubHandler("B", new TenantDataset("b.one", TenantDataCategory.Global))),
        ]);

        registry.Datasets.Select(d => d.Dataset.Name).Should().Equal("a.one", "a.two", "b.one");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(64 * 1024 - 1)]
    [InlineData(64 * 1024)]
    [InlineData(64 * 1024 + 1)]
    [InlineData(200_000)]
    public async Task Archive_round_trips_at_chunk_boundaries(int size)
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var plain = RandomNumberGenerator.GetBytes(size);
        using var encrypted = new MemoryStream();
        await ArchiveCrypto.EncryptAsync(new MemoryStream(plain), encrypted, key, default);

        using var decrypted = new MemoryStream();
        encrypted.Position = 0;
        await ArchiveCrypto.DecryptAsync(encrypted, decrypted, key, default);

        decrypted.ToArray().Should().Equal(plain);
    }

    [Fact]
    public async Task A_zip_written_straight_through_the_encrypting_stream_decrypts_to_the_same_archive()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var sink = new MemoryStream();
        // Disposing the encrypting stream disposes its destination, so the ciphertext is read back with ToArray.
        using (var sealedStream = ArchiveCrypto.CreateEncryptingStream(sink, key))
        using (var zip = new ZipArchive(sealedStream, ZipArchiveMode.Create, leaveOpen: false))
        {
            using (var entry = zip.CreateEntry("a.txt").Open())
                entry.Write(Encoding.UTF8.GetBytes(new string('a', 200_000)));
            using (var entry = zip.CreateEntry("b.txt").Open())
                entry.Write(Encoding.UTF8.GetBytes("bee"));
        }
        var encrypted = sink.ToArray();

        // The ciphertext hides the names and contents.
        Encoding.UTF8.GetString(encrypted).Should().NotContain("b.txt");

        using var plainZip = new MemoryStream();
        await ArchiveCrypto.DecryptAsync(new MemoryStream(encrypted), plainZip, key, default);
        plainZip.Position = 0;
        using var zip2 = new ZipArchive(plainZip, ZipArchiveMode.Read);
        zip2.Entries.Select(e => e.FullName).Should().Equal("a.txt", "b.txt");
        using var reader = new StreamReader(zip2.GetEntry("b.txt")!.Open());
        (await reader.ReadToEndAsync()).Should().Be("bee");
    }

    [Fact]
    public async Task Ciphertext_does_not_show_the_plaintext()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        using var encrypted = new MemoryStream();
        await ArchiveCrypto.EncryptAsync(new MemoryStream(Encoding.UTF8.GetBytes("secret customer name")), encrypted, key, default);

        Encoding.UTF8.GetString(encrypted.ToArray()).Should().NotContain("secret customer name");
    }

    [Fact]
    public async Task A_wrong_key_is_refused()
    {
        var encrypted = await EncryptedAsync(RandomNumberGenerator.GetBytes(32), "data");

        var act = () => ArchiveCrypto.DecryptAsync(new MemoryStream(encrypted), null, RandomNumberGenerator.GetBytes(32), default);

        await act.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task A_changed_byte_is_refused()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var encrypted = await EncryptedAsync(key, new string('x', 1000));
        encrypted[encrypted.Length / 2] ^= 0x01;

        var act = () => ArchiveCrypto.DecryptAsync(new MemoryStream(encrypted), null, key, default);

        await act.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task A_truncated_archive_is_refused()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var encrypted = await EncryptedAsync(key, new string('x', 1000));

        var act = () => ArchiveCrypto.DecryptAsync(new MemoryStream(encrypted, 0, encrypted.Length - 10), null, key, default);

        await act.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task Data_after_the_last_chunk_is_refused()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var encrypted = (await EncryptedAsync(key, "data")).Append((byte)0).ToArray();

        var act = () => ArchiveCrypto.DecryptAsync(new MemoryStream(encrypted), null, key, default);

        await act.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task A_file_that_is_not_an_archive_is_refused()
    {
        var act = () => ArchiveCrypto.DecryptAsync(new MemoryStream(Encoding.UTF8.GetBytes("PK\x03\x04 plain zip, not ours....")), null, RandomNumberGenerator.GetBytes(32), default);

        await act.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task Attachment_paths_cannot_leave_the_company_folder()
    {
        using var buffer = new MemoryStream();
        using var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true);
        var writer = new ExportArchiveWriter(zip, new MemoryFiles());

        foreach (var path in new[] { "../other-company/secret.pdf", "/etc/passwd", "a/../b", "a//b" })
        {
            var act = () => writer.AttachFileAsync(path);
            await act.Should().ThrowAsync<ArgumentException>(path);
        }
    }

    [Fact]
    public void Key_of_the_wrong_length_is_refused()
    {
        var act = () => ArchiveCrypto.ParseKey(Convert.ToBase64String(new byte[16]));

        act.Should().Throw<InvalidOperationException>().WithMessage("*32 bytes*");
    }

    private static async Task<byte[]> EncryptedAsync(byte[] key, string text)
    {
        using var output = new MemoryStream();
        await ArchiveCrypto.EncryptAsync(new MemoryStream(Encoding.UTF8.GetBytes(text)), output, key, default);
        return output.ToArray();
    }

    /// <summary>A handler that only declares datasets, for registry rules.</summary>
    internal sealed class StubHandler(string module, params TenantDataset[] datasets) : ITenantDataHandler
    {
        public string Module => module;

        public IReadOnlyList<TenantDataset> Datasets => datasets;

        public Task ExportAsync(TenantDataset dataset, ITenantExportWriter writer, CancellationToken ct) => throw new NotSupportedException();

        public Task<long> CountAsync(TenantDataset dataset, CancellationToken ct) => throw new NotSupportedException();

        public Task<long> DeleteAsync(TenantDataset dataset, CancellationToken ct) => throw new NotSupportedException();
    }
}
