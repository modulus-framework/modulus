using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Exceptions;
using Modulus.MultiTenancy;
using Modulus.Media;
using Modulus.Storage;
using Xunit;

namespace Modulus.Media.Tests;

/// <summary>Media library: content is checked by its bytes, names are sanitised, storage keys are the server's, and one company never sees another's files.</summary>
[Trait("Category", "Unit")]
public sealed class MediaLibraryTests
{
    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D];
    private static readonly byte[] PdfBytes = Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj\n<< >>\nendobj\n");

    private sealed class Harness
    {
        public FakeStorage Files { get; } = new();
        public InMemoryMediaStore Store { get; } = new();
        public CurrentTenant Tenant { get; } = new();
        public FakeUser User { get; } = new();
        public DateTimeOffset Now { get; set; } = new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);
        public long MaxBytes { get; set; } = 1024 * 1024;

        public MediaLibrary Build(IMediaStore? store = null)
            => new(Files, store ?? Store, Tenant, User, new FixedClock(this), Options.Create(new MediaOptions { MaxBytes = MaxBytes }));
    }

    [Fact]
    public async Task A_png_is_stored_under_a_server_key_with_its_hash_and_detected_type()
    {
        var h = new Harness();
        var library = h.Build();

        var item = await library.UploadAsync(new MemoryStream(PngBytes), "logo.png");

        item.Kind.Should().Be(MediaKind.Image);
        item.ContentType.Should().Be("image/png");
        item.Size.Should().Be(PngBytes.Length);
        item.Sha256.Should().Be(Convert.ToHexString(SHA256.HashData(PngBytes)).ToLowerInvariant());
        item.StorageKey.Should().Be($"media/host/{item.Id:N}.png");
        h.Files.Stored[item.StorageKey].Should().Equal(PngBytes);
    }

    [Fact]
    public async Task A_pdf_is_accepted_as_a_document()
    {
        var h = new Harness();

        var item = await h.Build().UploadAsync(new MemoryStream(PdfBytes), "contract.pdf");

        item.Kind.Should().Be(MediaKind.Document);
        item.ContentType.Should().Be("application/pdf");
        item.StorageKey.Should().EndWith(".pdf");
    }

    [Fact]
    public async Task The_uploader_and_company_are_recorded()
    {
        var h = new Harness();
        var company = Guid.NewGuid();
        var person = Guid.NewGuid();
        h.User.UserId = person;
        using var scope = h.Tenant.Change(new TenantInfo(company, "acme"));

        var item = await h.Build().UploadAsync(new MemoryStream(PngBytes), "a.png");

        item.TenantId.Should().Be(company);
        item.UploadedBy.Should().Be(person);
        item.UploadedAt.Should().Be(h.Now);
        item.StorageKey.Should().StartWith($"media/{company:N}/");
    }

    [Theory]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>")]
    [InlineData("<?php system($_GET['c']); ?>")]
    [InlineData("MZ this is an executable")]
    public async Task Files_that_are_not_an_accepted_image_or_pdf_are_refused(string text)
    {
        var h = new Harness();

        var act = () => h.Build().UploadAsync(new MemoryStream(Encoding.UTF8.GetBytes(text)), "x.png");

        await act.Should().ThrowAsync<ValidationException>();
        h.Files.Stored.Should().BeEmpty();
    }

    [Fact]
    public async Task An_empty_or_oversized_file_is_refused()
    {
        var h = new Harness { MaxBytes = PdfBytes.Length - 1 };
        var library = h.Build();

        await library.Invoking(l => l.UploadAsync(new MemoryStream(), "empty.pdf")).Should().ThrowAsync<ValidationException>();
        await library.Invoking(l => l.UploadAsync(new MemoryStream(PdfBytes), "big.pdf")).Should().ThrowAsync<ValidationException>();
        h.Files.Stored.Should().BeEmpty();
    }

    [Theory]
    [InlineData("..\\..\\evil.png", "evil.png")]
    [InlineData("../../etc/passwd.pdf", "passwd.pdf")]
    [InlineData("bad\u0000name\r\n.pdf", "badname.pdf")]
    [InlineData("..", "file")]
    [InlineData("   ", "file")]
    public async Task Display_names_keep_only_a_safe_leaf(string name, string expected)
    {
        var h = new Harness();

        var item = await h.Build().UploadAsync(new MemoryStream(PdfBytes), name);

        item.FileName.Should().Be(expected);
        item.StorageKey.Should().NotContain("evil").And.NotContain("passwd");
    }

    [Fact]
    public async Task Long_display_names_are_cut_to_the_limit()
    {
        var h = new Harness();

        var item = await h.Build().UploadAsync(new MemoryStream(PdfBytes), new string('n', 500) + ".pdf");

        item.FileName.Length.Should().Be(new MediaOptions().MaxFileNameLength);
    }

    [Fact]
    public async Task One_company_cannot_see_list_open_or_delete_another_companys_file()
    {
        var h = new Harness();
        var library = h.Build();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();

        MediaItem item;
        using (h.Tenant.Change(new TenantInfo(companyA, "a")))
            item = await library.UploadAsync(new MemoryStream(PngBytes), "a.png");

        using (h.Tenant.Change(new TenantInfo(companyB, "b")))
        {
            (await library.ListAsync(new MediaQuery())).Total.Should().Be(0);
            await library.Invoking(l => l.GetAsync(item.Id)).Should().ThrowAsync<NotFoundException>();
            await library.Invoking(l => l.OpenAsync(item)).Should().ThrowAsync<NotFoundException>();
            await library.Invoking(l => l.DeleteAsync(item.Id)).Should().ThrowAsync<NotFoundException>();
        }

        h.Files.Stored.Should().ContainKey(item.StorageKey);
    }

    [Fact]
    public async Task Delete_removes_the_metadata_and_the_bytes()
    {
        var h = new Harness();
        var library = h.Build();
        var item = await library.UploadAsync(new MemoryStream(PdfBytes), "c.pdf");

        await library.DeleteAsync(item.Id);

        (await library.ListAsync(new MediaQuery())).Total.Should().Be(0);
        h.Files.Stored.Should().NotContainKey(item.StorageKey);
    }

    [Fact]
    public async Task If_the_metadata_cannot_be_stored_the_uploaded_bytes_are_removed()
    {
        var h = new Harness();
        var library = h.Build(new FailingStore());

        var act = () => library.UploadAsync(new MemoryStream(PngBytes), "orphan.png");

        await act.Should().ThrowAsync<InvalidOperationException>();
        h.Files.Stored.Should().BeEmpty();
    }

    [Fact]
    public async Task Listing_filters_by_kind_and_search_and_pages_with_a_total()
    {
        var h = new Harness();
        var library = h.Build();
        await library.UploadAsync(new MemoryStream(PngBytes), "Invoice-1.png");
        await library.UploadAsync(new MemoryStream(PngBytes), "logo.png");
        await library.UploadAsync(new MemoryStream(PdfBytes), "Invoice-2.pdf");

        (await library.ListAsync(new MediaQuery(Kind: MediaKind.Document))).Total.Should().Be(1);
        (await library.ListAsync(new MediaQuery(Search: "invoice"))).Total.Should().Be(2);

        var page = await library.ListAsync(new MediaQuery(PageSize: 2, Page: 2));
        page.Total.Should().Be(3);
        page.Items.Should().HaveCount(1);
    }

    private sealed class FixedClock(Harness harness) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => harness.Now;
    }

    private sealed class FakeUser : ICurrentUser
    {
        public Guid? UserId { get; set; }
        public string? UserName => null;
        public string? Email => null;
        public bool IsAuthenticated => UserId.HasValue;
        public bool IsInRole(string role) => false;
        public bool HasPermission(string permission) => false;
        public IReadOnlyList<string> Permissions => [];
    }

    private sealed class FailingStore : IMediaStore
    {
        public Task AddAsync(MediaItem item, CancellationToken ct = default) => throw new InvalidOperationException("database down");
        public Task<MediaItem?> GetAsync(Guid? tenantId, Guid id, CancellationToken ct = default) => Task.FromResult<MediaItem?>(null);
        public Task<MediaPage> QueryAsync(Guid? tenantId, MediaQuery query, CancellationToken ct = default) => Task.FromResult(new MediaPage([], 0));
        public Task<bool> RemoveAsync(Guid? tenantId, Guid id, CancellationToken ct = default) => Task.FromResult(false);
    }

    private sealed class FakeStorage : IFileStorage
    {
        public Dictionary<string, byte[]> Stored { get; } = new(StringComparer.Ordinal);

        public async Task UploadAsync(string path, Stream content, string? contentType = null, CancellationToken ct = default)
        {
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, ct);
            Stored[path] = buffer.ToArray();
        }

        public Task DeleteAsync(string path, CancellationToken ct = default)
        {
            Stored.Remove(path);
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(string path, CancellationToken ct = default) => Task.FromResult(Stored.ContainsKey(path));
        public Task<Stream> DownloadAsync(string path, CancellationToken ct = default) => Task.FromResult<Stream>(new MemoryStream(Stored[path]));
        public Task<string> GetPresignedUrlAsync(string path, TimeSpan expiry, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<string> GetPresignedUploadUrlAsync(string path, TimeSpan expiry, string? contentType = null, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
